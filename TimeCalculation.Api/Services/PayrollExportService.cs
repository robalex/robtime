using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Orchestrates a payroll export run: fetch (approved timecard snapshots, this profile's mapping
/// config) → decide (the already-pure PayrollExportProjector) → persist (one PayrollExportBatch,
/// file bytes included). No new pay logic lives here — every dollar figure was already decided by
/// the projector; this class's only real job is assembling its input from the database and turning
/// its output into a file, per CLAUDE.md's service/logic split.
///
/// Like PunchImportService, there is no database transaction anywhere in this method — "all-or-
/// nothing" means every gate (profile exists, no prior batch, approvals exist, snapshots parse,
/// projection is complete) is checked and can fail before a single row is written, not that writes
/// are rolled back after the fact. The last gate — projection.IsComplete — is what actually protects
/// against a partial export: nothing is persisted while any line is unmapped or any employee lacks
/// a resolvable external id.
/// </summary>
public class PayrollExportService(PayrollDbContext db, IClock clock, ITenantContextAccessor tenantContext)
{
    public async Task<ServiceResult<PayrollExportBatch>> CreateExportAsync(
        int profileId, CreatePayrollExportRequest request, string actorUserId, CancellationToken ct)
    {
        var errors = PayrollExportRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollExportBatch>.ValidationFailed(errors);
        }

        var periodStart = request.PeriodStart;
        var periodEnd = request.PeriodEnd;

        var clientId = tenantContext.ClientId;
        if (clientId is null)
        {
            return ServiceResult<PayrollExportBatch>.Forbidden("No client is selected for this request.");
        }

        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
        {
            return ServiceResult<PayrollExportBatch>.NotFound($"No payroll export profile with id {profileId}.");
        }

        var priorBatch = await db.PayrollExportBatches.FirstOrDefaultAsync(
            b => b.ProfileId == profileId && b.PeriodStart == periodStart && b.PeriodEnd == periodEnd
                && b.VoidedAt == null, ct);
        if (priorBatch is not null)
        {
            return ServiceResult<PayrollExportBatch>.Conflict(
                $"Batch {priorBatch.Id} already covers {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd} " +
                "for this profile and has not been voided. Void it before re-exporting this period.");
        }

        // Exact-period match, mirroring TimecardService.ActiveApprovalAsync's own per-employee
        // convention rather than an overlap query — see the class doc comment's phase-one note. An
        // employee on a different PayPeriodFrequency whose approval doesn't land on this exact
        // boundary simply isn't included in this run.
        var approvals = await db.TimecardApprovals
            .Where(a => a.ClientId == clientId.Value && a.UnapprovedAt == null
                && a.PeriodStart == periodStart && a.PeriodEnd == periodEnd)
            .ToListAsync(ct);
        if (approvals.Count == 0)
        {
            return ServiceResult<PayrollExportBatch>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["period"] = [$"No approved timecards cover exactly {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}."],
            });
        }

        // Every approval's snapshot must parse before anything is written — a corrupt row silently
        // excluding one employee's pay is exactly the failure mode this whole effort exists to
        // prevent, so it fails the whole batch loudly instead.
        var snapshots = new List<PayCalculationSnapshot>();
        var corruptEmployeeIds = new List<int>();
        foreach (var approval in approvals)
        {
            try
            {
                snapshots.Add(TimecardSnapshotSerializer.Deserialize(approval.SnapshotJson));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                corruptEmployeeIds.Add(approval.EmployeeId);
            }
        }

        if (corruptEmployeeIds.Count > 0)
        {
            return ServiceResult<PayrollExportBatch>.Conflict(
                "The stored snapshot for the following employee(s) could not be read and must be " +
                $"investigated before this period can be exported: {string.Join(", ", corruptEmployeeIds)}.");
        }

        var mappings = await db.PayrollEarningCodeMappings.Where(m => m.ProfileId == profileId).ToListAsync(ct);
        var externalIds = await db.PayrollEmployeeIdentifiers
            .Where(i => i.ProfileId == profileId)
            .ToDictionaryAsync(i => i.EmployeeId, i => i.ExternalEmployeeId, ct);

        var projection = PayrollExportProjector.Project(new PayrollExportProjectionInput
        {
            Snapshots = snapshots,
            Mappings = mappings,
            ExternalIdsByEmployeeId = externalIds,
            Grouping = profile.Grouping,
            Rounding = new PayrollExportRounding
            {
                AmountScale = profile.AmountScale,
                HoursScale = profile.HoursScale,
                Policy = profile.RoundingPolicy,
                AdjustmentEarningCode = profile.AdjustmentEarningCode,
            },
        });

        if (!projection.IsComplete)
        {
            return ServiceResult<PayrollExportBatch>.Conflict(DescribeIncompleteProjection(projection));
        }

        var now = clock.GetCurrentInstant();
        var batch = new PayrollExportBatch
        {
            ClientId = clientId.Value,
            ProfileId = profileId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            EmployeeCount = projection.Rows.Select(r => r.EmployeeId).Distinct().Count(),
            RowCount = projection.Rows.Count,
            TotalAmount = projection.Rows.Sum(r => r.Amount),
            FileName = $"payroll-export-{profileId}-{periodStart:yyyy-MM-dd}-{periodEnd:yyyy-MM-dd}.csv",
            FileContent = PayrollExportCsvWriter.Write(projection.Rows),
            ExportedByUserId = actorUserId,
            ExportedAt = now,
        };

        // Nothing downstream needs this row's generated id (unlike PunchImportBatch, which its child
        // Punch rows reference) — the file is already baked into this same row, so one save covers it.
        db.PayrollExportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollExportBatch>.Success(batch);
    }

    public async Task<ServiceResult<PagedResult<PayrollExportBatch>>> ListBatchesAsync(
        int profileId, PagingQuery paging, CancellationToken ct)
    {
        var profileExists = await db.PayrollExportProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
        {
            return ServiceResult<PagedResult<PayrollExportBatch>>.NotFound($"No payroll export profile with id {profileId}.");
        }

        var query = db.PayrollExportBatches.Where(b => b.ProfileId == profileId).OrderByDescending(b => b.ExportedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return ServiceResult<PagedResult<PayrollExportBatch>>.Success(new PagedResult<PayrollExportBatch>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        });
    }

    /// <summary>Includes FileContent — only the download handler should call this; ListBatchesAsync
    /// deliberately leaves the byte column out of its DTO projection.</summary>
    public async Task<ServiceResult<PayrollExportBatch>> GetBatchAsync(int profileId, int id, CancellationToken ct)
    {
        var batch = await db.PayrollExportBatches.FirstOrDefaultAsync(b => b.Id == id && b.ProfileId == profileId, ct);
        return batch is null
            ? ServiceResult<PayrollExportBatch>.NotFound($"No export batch with id {id} for profile {profileId}.")
            : ServiceResult<PayrollExportBatch>.Success(batch);
    }

    public async Task<ServiceResult<PayrollExportBatch>> VoidBatchAsync(
        int profileId, int id, string actorUserId, CancellationToken ct)
    {
        var batch = await db.PayrollExportBatches.FirstOrDefaultAsync(b => b.Id == id && b.ProfileId == profileId, ct);
        if (batch is null)
        {
            return ServiceResult<PayrollExportBatch>.NotFound($"No export batch with id {id} for profile {profileId}.");
        }

        if (batch.VoidedAt is not null)
        {
            return ServiceResult<PayrollExportBatch>.Conflict($"Export batch {id} was already voided.");
        }

        batch.VoidedByUserId = actorUserId;
        batch.VoidedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollExportBatch>.Success(batch);
    }

    private static string DescribeIncompleteProjection(PayrollExportProjection projection)
    {
        var parts = new List<string>();

        if (projection.UnmappedLines.Count > 0)
        {
            var codes = projection.UnmappedLines.Select(u => $"{u.LineType}/'{u.LineCode}'");
            parts.Add($"unmapped earning code(s): {string.Join(", ", codes)}");
        }

        if (projection.EmployeesMissingExternalId.Count > 0)
        {
            parts.Add(
                "employee(s) with no identifier for this profile: " +
                string.Join(", ", projection.EmployeesMissingExternalId));
        }

        return "This period cannot be exported yet — " + string.Join("; ", parts) +
            ". Configure the missing mapping(s)/identifier(s) before exporting.";
    }
}
