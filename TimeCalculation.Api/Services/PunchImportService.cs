using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Bulk CSV punch import — orchestrates PunchImportCsvReader (parse) and PunchImportRowValidator
/// (per-row validation) into an all-or-nothing write: every row is validated before anything touches
/// the database, so one bad row can never leave the other ten thousand silently imported. Deliberately
/// thin — parsing and validation are already someone else's job (single responsibility), this class's
/// only real work is fetching the lookups those need once, deciding whether the whole file passes, and
/// persisting.
///
/// ClientId is never taken from the request — it comes from ITenantContextAccessor, the exact same
/// resolution PayrollDbContext's own query filters already use (JWT claim for everyone but
/// SystemAdmin, the tenant-selection header for SystemAdmin). A client-supplied ClientId field would
/// let a write choose a tenant the read-side filters could never have shown the caller in the first
/// place — not a risk worth taking for a feature that can create thousands of rows in one call.
/// </summary>
public class PunchImportService(
    PayrollDbContext db, IClock clock, TimecardLockService lockService, ITenantContextAccessor tenantContext)
{
    public async Task<ServiceResult<PunchImportBatchResponse>> ImportAsync(
        Stream csvStream, string fileName, string importedByUserId, CancellationToken ct)
    {
        var clientId = tenantContext.ClientId;
        if (clientId is null)
        {
            return ServiceResult<PunchImportBatchResponse>.Forbidden(
                "No client is selected for this request.");
        }

        var parsed = PunchImportCsvReader.Parse(csvStream);
        if (parsed.FileError is not null)
        {
            return ServiceResult<PunchImportBatchResponse>.ValidationFailed(parsed.FileError);
        }

        if (parsed.Rows.Count == 0)
        {
            return ServiceResult<PunchImportBatchResponse>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["file"] = ["The file has a header row but no data rows."],
            });
        }

        // Fetched once for the whole file (already scoped to this tenant by the DbContext's own
        // query filters) rather than once per row — the entire reason a bulk import needs its own
        // validator instead of just calling PunchRequestValidator.Validate in a loop. Full Employee
        // rows, not just ids: the validator needs each one's HomeTimeZoneId as the fallback zone for
        // resolving a row's local PunchTime when the file doesn't specify one itself.
        var employeesById = await db.Employees.ToDictionaryAsync(e => e.Id, ct);
        var validPositionIds = (await db.Positions.Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var errors = new Dictionary<string, string[]>();
        var requests = new List<CreatePunchRequest>();
        foreach (var row in parsed.Rows)
        {
            var result = PunchImportRowValidator.Validate(row, employeesById, validPositionIds);
            if (result.Kind == ServiceResultKind.Success)
            {
                requests.Add(result.Value!);
            }
            else
            {
                foreach (var (field, messages) in result.ValidationErrors!)
                {
                    errors[$"row[{row.RowNumber}].{field}"] = messages;
                }
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult<PunchImportBatchResponse>.ValidationFailed(errors);
        }

        // Every EmployeeId here is already confirmed valid above, so employeesById is guaranteed to
        // have an entry for each request below.
        var lockTargets = requests.Select(r => new LockCheckTarget(employeesById[r.EmployeeId], r.PunchTime)).ToList();
        if (await lockService.CheckBulkAsync(lockTargets, ct) is { } lockError)
        {
            return ServiceResult<PunchImportBatchResponse>.Conflict(lockError);
        }

        var now = clock.GetCurrentInstant();
        var batch = new PunchImportBatch
        {
            ClientId = clientId.Value,
            FileName = fileName,
            PunchCount = requests.Count,
            ImportedByUserId = importedByUserId,
            ImportedAt = now,
        };
        db.PunchImportBatches.Add(batch);
        // Needs its own SaveChanges — the punches below need the batch's real, DB-generated id to
        // tag themselves with, same two-phase reasoning as the punch-then-audit save further down.
        await db.SaveChangesAsync(ct);

        var punches = requests.Select(request => new Punch
        {
            ClientId = clientId.Value,
            EmployeeId = request.EmployeeId,
            PunchTime = request.PunchTime,
            PunchTimeZoneId = request.PunchTimeZoneId ?? "UTC",
            Kind = request.Kind,
            Subtype = request.Subtype,
            PositionId = request.PositionId,
            Amount = request.Amount,
            Hours = request.Hours,
            BonusKind = request.BonusKind,
            CountsTowardRegularRate = request.CountsTowardRegularRate,
            CreatedAt = now,
            CreatedBy = importedByUserId,
            ImportBatchId = batch.Id,
        }).ToList();

        db.Punches.AddRange(punches);
        await db.SaveChangesAsync(ct);

        // Same two-phase save as every other punch-creation path: audit entries need the punches'
        // real, DB-generated ids.
        db.PunchAudits.AddRange(punches.Select(p => PunchAuditor.Created(p, importedByUserId, now)));
        await db.SaveChangesAsync(ct);

        return ServiceResult<PunchImportBatchResponse>.Success(PunchImportBatchResponse.FromEntity(batch));
    }

    public async Task<PagedResult<PunchImportBatchResponse>> ListBatchesAsync(PagingQuery paging, CancellationToken ct)
    {
        var query = db.PunchImportBatches.OrderByDescending(b => b.ImportedAt);
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<PunchImportBatchResponse>
        {
            Items = items.Select(PunchImportBatchResponse.FromEntity).ToList(),
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    /// <summary>Hard-deletes every Punch this batch produced — a real DELETE, not the usual
    /// IsDeleted soft-delete every other tenant-scoped entity gets (see PunchImportBatch's own doc
    /// comment for why: a batch can be large enough that a soft-deleted-but-still-present pile of
    /// bad punches is real, pointless bloat). IgnoreQueryFilters so this also catches any punch from
    /// the batch that was already individually soft-deleted before the whole batch was — if the
    /// batch is being wiped out, an orphaned soft-deleted row from it shouldn't survive either.
    /// PunchAuditEntry rows the import wrote are deliberately left alone; the batch row itself is
    /// marked deleted, never removed.</summary>
    public async Task<ServiceResult<PunchImportBatchResponse>> DeleteBatchAsync(
        int id, string actorUserId, CancellationToken ct)
    {
        var batch = await db.PunchImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
        {
            return ServiceResult<PunchImportBatchResponse>.NotFound($"No import batch with id {id}.");
        }

        if (batch.DeletedAt is not null)
        {
            return ServiceResult<PunchImportBatchResponse>.Conflict($"Import batch {id} was already deleted.");
        }

        var punches = await db.Punches.IgnoreQueryFilters()
            .Where(p => p.ClientId == batch.ClientId && p.ImportBatchId == id)
            .ToListAsync(ct);
        db.Punches.RemoveRange(punches);

        batch.DeletedByUserId = actorUserId;
        batch.DeletedAt = clock.GetCurrentInstant();

        await db.SaveChangesAsync(ct);

        return ServiceResult<PunchImportBatchResponse>.Success(PunchImportBatchResponse.FromEntity(batch));
    }
}
