using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PunchService(PayrollDbContext db, IClock clock, TimecardLockService lockService)
{
    public async Task<ServiceResult<Punch>> CreateAsync(CreatePunchRequest request, string createdBy, CancellationToken ct)
    {
        var errors = PunchRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Punch>.ValidationFailed(errors);
        }

        // Fetched (not just AnyAsync) because the punch needs the employee's ClientId — the
        // request only carries EmployeeId, and deriving ClientId server-side from the employee
        // record avoids trusting a client-supplied ClientId that might not actually match.
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null)
        {
            return ServiceResult<Punch>.NotFound($"No employee with id {request.EmployeeId}.");
        }

        if (await lockService.CheckAsync(employee, request.PunchTime, ct) is { } lockError)
        {
            return ServiceResult<Punch>.Conflict(lockError);
        }

        if (request.PositionId is { } positionId)
        {
            var positionExists = await db.Positions.AnyAsync(p => p.Id == positionId, ct);
            if (!positionExists)
            {
                return ServiceResult<Punch>.NotFound($"No position with id {positionId}.");
            }
        }

        var punch = new Punch
        {
            ClientId = employee.ClientId,
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
            CreatedAt = clock.GetCurrentInstant(),
            CreatedBy = createdBy,
            DeviceId = request.DeviceId,
            DevicePunchId = request.DevicePunchId,
        };

        db.Punches.Add(punch);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (request.DeviceId is not null && request.DevicePunchId is not null)
        {
            return ServiceResult<Punch>.Conflict(
                $"A punch from device {request.DeviceId} with device punch id {request.DevicePunchId} already exists for employee {request.EmployeeId}.");
        }

        // A second SaveChanges, not combined with the one above: PunchAuditEntry.PunchId needs the
        // punch's real, DB-generated Id, which doesn't exist until after the insert above completes
        // (Punch has no navigation property EF could use to fix the FK up automatically — see
        // PunchAuditor's own doc comment).
        db.PunchAudits.Add(PunchAuditor.Created(punch, createdBy, punch.CreatedAt));
        await db.SaveChangesAsync(ct);

        return ServiceResult<Punch>.Success(punch);
    }

    /// <summary>
    /// Phase 6.8's fast bulk-entry grid: one request for a whole batch of rows rather than one
    /// <c>POST /punches</c> per row. All-or-nothing — every row is validated (shape, lock, position
    /// existence) before anything touches the database, so a typo on row 6 of 7 can never leave the
    /// other six silently saved. Every row must share one EmployeeId; this isn't a general
    /// cross-employee bulk-insert, it's one supervisor entering one employee's stretch of time.
    /// </summary>
    public async Task<ServiceResult<List<Punch>>> CreateBatchAsync(
        List<CreatePunchRequest> requests, string createdBy, CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return ServiceResult<List<Punch>>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["punches"] = ["At least one punch is required."],
            });
        }

        var employeeIds = requests.Select(r => r.EmployeeId).Distinct().ToList();
        if (employeeIds.Count > 1)
        {
            return ServiceResult<List<Punch>>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["punches"] = ["All punches in a batch must belong to the same employee."],
            });
        }

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < requests.Count; i++)
        {
            foreach (var (field, messages) in PunchRequestValidator.Validate(requests[i]))
            {
                errors[$"punches[{i}].{field}"] = messages;
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult<List<Punch>>.ValidationFailed(errors);
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeIds[0], ct);
        if (employee is null)
        {
            return ServiceResult<List<Punch>>.NotFound($"No employee with id {employeeIds[0]}.");
        }

        foreach (var request in requests)
        {
            if (await lockService.CheckAsync(employee, request.PunchTime, ct) is { } lockError)
            {
                return ServiceResult<List<Punch>>.Conflict(lockError);
            }
        }

        var positionIds = requests.Where(r => r.PositionId is not null).Select(r => r.PositionId!.Value).Distinct().ToList();
        if (positionIds.Count > 0)
        {
            var existingPositionIds = await db.Positions
                .Where(p => positionIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            var missing = positionIds.Except(existingPositionIds).ToList();
            if (missing.Count > 0)
            {
                return ServiceResult<List<Punch>>.NotFound($"No position with id {missing[0]}.");
            }
        }

        var now = clock.GetCurrentInstant();
        var punches = requests.Select(request => new Punch
        {
            ClientId = employee.ClientId,
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
            CreatedBy = createdBy,
            DeviceId = request.DeviceId,
            DevicePunchId = request.DevicePunchId,
        }).ToList();

        db.Punches.AddRange(punches);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<List<Punch>>.Conflict(
                "One or more punches in this batch duplicate an existing device punch.");
        }

        // Same two-phase save as CreateAsync: audit entries need the punches' real, DB-generated ids.
        db.PunchAudits.AddRange(punches.Select(p => PunchAuditor.Created(p, createdBy, now)));
        await db.SaveChangesAsync(ct);

        return ServiceResult<List<Punch>>.Success(punches);
    }

    /// <summary>
    /// Whether this employee is currently on the clock: their most recent clock punch decides it —
    /// an <see cref="PunchKind.In"/> means on, an <see cref="PunchKind.Out"/> (or no clock punches at
    /// all) means off. FixedDollar/FixedHours entries are skipped; they're payroll adjustments, not
    /// clock events.
    ///
    /// Deliberately unbounded in time — no "only look back N hours" cutoff. An In from three days
    /// ago with no Out really does mean this employee is still shown as on the clock, and saying so
    /// ("Clocked in since Tuesday") surfaces the missed punch instead of hiding it behind a silent
    /// window that would report them clocked out and let a second In pile up. The query itself is
    /// still bounded — one row, ordered by the {ClientId, EmployeeId, PunchTime} index.
    /// </summary>
    public async Task<ClockStatusResponse> GetClockStatusAsync(int employeeId, CancellationToken ct)
    {
        var lastClockPunch = await db.Punches
            .Where(p => p.EmployeeId == employeeId && (p.Kind == PunchKind.In || p.Kind == PunchKind.Out))
            .OrderByDescending(p => p.PunchTime)
            .FirstOrDefaultAsync(ct);

        var isClockedIn = lastClockPunch?.Kind == PunchKind.In;
        return new ClockStatusResponse
        {
            EmployeeId = employeeId,
            IsClockedIn = isClockedIn,
            Since = isClockedIn ? lastClockPunch!.PunchTime : null,
            PositionId = isClockedIn ? lastClockPunch!.PositionId : null,
            SincePunchId = isClockedIn ? lastClockPunch!.Id : null,
        };
    }

    /// <summary>
    /// Scoped by EmployeeId (required, not optional) plus an optional PunchTime range — Punches is
    /// "the hottest, largest table in the system" (PayrollDbContext's own class doc comment); an
    /// unbounded "every punch for this client" query has no legitimate caller and would ignore the
    /// {ClientId, EmployeeId, PunchTime} index this shape is built to use.
    /// </summary>
    public async Task<PagedResult<Punch>> ListAsync(
        int employeeId, Instant? from, Instant? to, PagingQuery paging, CancellationToken ct)
    {
        var query = db.Punches.Where(p => p.EmployeeId == employeeId);
        if (from is { } fromInstant)
        {
            query = query.Where(p => p.PunchTime >= fromInstant);
        }

        if (to is { } toInstant)
        {
            query = query.Where(p => p.PunchTime <= toInstant);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.PunchTime)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<Punch>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<Punch>> GetAsync(int id, CancellationToken ct)
    {
        var punch = await db.Punches.FirstOrDefaultAsync(p => p.Id == id, ct);
        return punch is null
            ? ServiceResult<Punch>.NotFound($"No punch with id {id}.")
            : ServiceResult<Punch>.Success(punch);
    }

    /// <summary>Direct edit only — no RequirePunchEditApproval branch yet (that's Phase 6.2's
    /// PunchChangeRequest flow; this always applies the change and writes the audit entry itself).
    /// </summary>
    public async Task<ServiceResult<Punch>> UpdateAsync(
        int id, UpdatePunchRequest request, string actorUserId, CancellationToken ct)
    {
        var existing = await db.Punches.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (existing is null)
        {
            return ServiceResult<Punch>.NotFound($"No punch with id {id}.");
        }

        // Employee, not just the punch's own EmployeeId, because the lock check needs their
        // HomeTimeZoneId to resolve which calendar date (and therefore which pay period) the punch
        // falls in — same reasoning CreateAsync's employee fetch already has.
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == existing.EmployeeId, ct)
            ?? throw new InvalidOperationException($"Punch {id} references employee {existing.EmployeeId}, which does not exist.");

        // Checks the punch's current date and, if the edit moves it, its requested new date too —
        // an edit that reads from a locked period or writes into one is blocked either way (UI_PLAN.md
        // §8: locking covers every punch-mutating path, not just "the obvious" one).
        var lockError = await lockService.CheckAsync(employee, existing.PunchTime, ct);
        if (lockError is null && request.PunchTime is { } newTime && newTime != existing.PunchTime)
        {
            lockError = await lockService.CheckAsync(employee, newTime, ct);
        }

        if (lockError is not null)
        {
            return ServiceResult<Punch>.Conflict(lockError);
        }

        if (request.PositionId is { } positionId)
        {
            var positionExists = await db.Positions.AnyAsync(p => p.Id == positionId, ct);
            if (!positionExists)
            {
                return ServiceResult<Punch>.NotFound($"No position with id {positionId}.");
            }
        }

        var updated = PunchRequestMapper.ApplyUpdate(existing, request);
        var errors = PunchRequestValidator.ValidateConsistency(updated);
        if (errors.Count > 0)
        {
            return ServiceResult<Punch>.ValidationFailed(errors);
        }

        // Built before SetValues below, not after: SetValues applies updated's values onto
        // existing's own backing fields in place (EF supports this for init-only/record properties
        // the same way it materializes them), so existing would no longer hold the pre-edit values
        // once that call runs — this is the only point where "before" is still actually available.
        var auditEntry = PunchAuditor.Edited(existing, updated, actorUserId, clock.GetCurrentInstant(), request.Reason);

        db.Entry(existing).CurrentValues.SetValues(updated);
        db.PunchAudits.Add(auditEntry);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Punch>.Success(updated);
    }

    /// <summary>Soft-delete (IsDeleted = true, filtered out by PayrollDbContext's own query filter),
    /// same convention as every other tenant-scoped entity — never a hard DELETE.</summary>
    public async Task<ServiceResult<Punch>> DeleteAsync(int id, string actorUserId, string? reason, CancellationToken ct)
    {
        var existing = await db.Punches.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (existing is null)
        {
            return ServiceResult<Punch>.NotFound($"No punch with id {id}.");
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == existing.EmployeeId, ct)
            ?? throw new InvalidOperationException($"Punch {id} references employee {existing.EmployeeId}, which does not exist.");
        if (await lockService.CheckAsync(employee, existing.PunchTime, ct) is { } lockError)
        {
            return ServiceResult<Punch>.Conflict(lockError);
        }

        var deleted = existing with { IsDeleted = true };

        // Same "capture before SetValues" ordering as UpdateAsync above.
        var auditEntry = PunchAuditor.Deleted(existing, actorUserId, clock.GetCurrentInstant(), reason);

        db.Entry(existing).CurrentValues.SetValues(deleted);
        db.PunchAudits.Add(auditEntry);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Punch>.Success(deleted);
    }
}
