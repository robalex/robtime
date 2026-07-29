using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PunchChangeRequestService(PayrollDbContext db, IClock clock, TimecardLockService lockService)
{
    public async Task<ServiceResult<PunchChangeRequest>> SubmitAsync(
        SubmitPunchChangeRequestRequest request, string requesterUserId, int? callerEmployeeId, CancellationToken ct)
    {
        var errors = PunchChangeRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PunchChangeRequest>.ValidationFailed(errors);
        }

        int clientId;
        int employeeId;
        Employee employee;
        Instant? currentPunchTime = null;

        // Add has no existing punch to derive ClientId/EmployeeId from, so it looks up the named
        // Employee instead; Edit/Delete always take both from the target punch, ignoring whatever
        // EmployeeId the caller may have also sent (PunchChangeRequestValidator doesn't reject it,
        // but it's not authoritative for those two kinds).
        if (request.ChangeKind == PunchChangeKind.Add)
        {
            var addEmployee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId!.Value, ct);
            if (addEmployee is null)
            {
                return ServiceResult<PunchChangeRequest>.NotFound($"No employee with id {request.EmployeeId}.");
            }

            employee = addEmployee;
            clientId = employee.ClientId;
            employeeId = employee.Id;
        }
        else
        {
            var targetPunch = await db.Punches.FirstOrDefaultAsync(p => p.Id == request.PunchId!.Value, ct);
            if (targetPunch is null)
            {
                return ServiceResult<PunchChangeRequest>.NotFound($"No punch with id {request.PunchId}.");
            }

            clientId = targetPunch.ClientId;
            employeeId = targetPunch.EmployeeId;
            currentPunchTime = targetPunch.PunchTime;

            var editEmployee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (editEmployee is null)
            {
                return ServiceResult<PunchChangeRequest>.NotFound($"No employee with id {employeeId}.");
            }

            employee = editEmployee;
        }

        // An Employee caller (callerEmployeeId set) may only submit requests that target their own
        // record — Supervisor+ (callerEmployeeId null) is unrestricted. Checked here rather than
        // before this point because Edit/Delete's target employee isn't known until the punch lookup
        // above resolves it; the endpoint's own ResolveCallerScopeAsync only rules out an Employee
        // caller with no linked record at all.
        if (callerEmployeeId is { } restrictedToEmployeeId && restrictedToEmployeeId != employeeId)
        {
            return ServiceResult<PunchChangeRequest>.Forbidden("You can only submit change requests for your own punches.");
        }

        // Blocks a request against a locked period the same way a direct edit is blocked (UI_PLAN.md
        // §8) — submitting is itself a punch-mutating path, not just the eventual approval that
        // applies it. Checks the target's current date, and for Edit/Add, the requested new date too
        // (an Add lands entirely on its requested time; an Edit could move a punch into a locked
        // period even if its current one is open).
        var lockError = currentPunchTime is { } existingTime
            ? await lockService.CheckAsync(employee, existingTime, ct)
            : null;
        if (lockError is null && request.PunchTime is { } requestedTime
            && requestedTime != currentPunchTime)
        {
            lockError = await lockService.CheckAsync(employee, requestedTime, ct);
        }

        if (lockError is not null)
        {
            return ServiceResult<PunchChangeRequest>.Conflict(lockError);
        }

        if (request.PositionId is { } positionId)
        {
            var positionExists = await db.Positions.AnyAsync(p => p.Id == positionId, ct);
            if (!positionExists)
            {
                return ServiceResult<PunchChangeRequest>.NotFound($"No position with id {positionId}.");
            }
        }

        var changeRequest = new PunchChangeRequest
        {
            ClientId = clientId,
            EmployeeId = employeeId,
            PunchId = request.PunchId,
            ChangeKind = request.ChangeKind,
            RequestedPunchTime = request.PunchTime,
            RequestedPunchTimeZoneId = request.PunchTimeZoneId,
            RequestedKind = request.Kind,
            RequestedSubtype = request.Subtype,
            RequestedPositionId = request.PositionId,
            RequestedAmount = request.Amount,
            RequestedHours = request.Hours,
            RequestedBonusKind = request.BonusKind,
            RequestedCountsTowardRegularRate = request.CountsTowardRegularRate,
            RequesterUserId = requesterUserId,
            Reason = request.Reason,
            CreatedAt = clock.GetCurrentInstant(),
            Status = PunchChangeRequestStatus.Pending,
        };

        db.PunchChangeRequests.Add(changeRequest);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PunchChangeRequest>.Success(changeRequest);
    }

    public async Task<PagedResult<PunchChangeRequestResponse>> ListAsync(
        PunchChangeRequestStatus? status, int? employeeId, int? callerEmployeeId, PagingQuery paging, CancellationToken ct)
    {
        var query = db.PunchChangeRequests.AsQueryable();
        if (status is { } statusFilter)
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        // An Employee caller only ever sees their own requests, regardless of what employeeId (if
        // any) they passed — simpler and safer than rejecting a mismatched filter on a read-only
        // list, unlike SubmitAsync's Forbidden for the same situation on a write.
        var effectiveEmployeeId = callerEmployeeId ?? employeeId;
        if (effectiveEmployeeId is { } employeeIdFilter)
        {
            query = query.Where(r => r.EmployeeId == employeeIdFilter);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<PunchChangeRequestResponse>
        {
            Items = await EnrichAsync(items, ct),
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<PunchChangeRequestResponse>> GetAsync(int id, int? callerEmployeeId, CancellationToken ct)
    {
        var changeRequest = await db.PunchChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (changeRequest is null)
        {
            return ServiceResult<PunchChangeRequestResponse>.NotFound($"No punch change request with id {id}.");
        }

        if (callerEmployeeId is { } restrictedToEmployeeId && restrictedToEmployeeId != changeRequest.EmployeeId)
        {
            return ServiceResult<PunchChangeRequestResponse>.Forbidden("You can only view your own punch change requests.");
        }

        var enriched = await EnrichAsync([changeRequest], ct);
        return ServiceResult<PunchChangeRequestResponse>.Success(enriched[0]);
    }

    /// <summary>
    /// Batch-resolves the Employee/current-Punch display data a review queue needs — a bare
    /// PunchChangeRequest carries ids, not names or current values, and a reviewer deciding an Edit
    /// or Delete needs to see what they'd actually be changing, not just the requested new values in
    /// isolation. IgnoreQueryFilters on both lookups so a request against a since-soft-deleted
    /// employee or punch still renders (a reviewer should be able to see what they'd be denying),
    /// with the tenant condition restated explicitly per this codebase's IgnoreQueryFilters
    /// convention (see ClientService.VisibleTo) — provably redundant today, since every request in
    /// <paramref name="requests"/> already passed through PunchChangeRequests' own tenant filter and
    /// a request's EmployeeId/PunchId are only ever set from within-tenant lookups at submission
    /// time, but restated anyway rather than relying on that invariant silently.
    /// </summary>
    private async Task<List<PunchChangeRequestResponse>> EnrichAsync(
        List<PunchChangeRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var clientId = requests[0].ClientId;
        var employeeIds = requests.Select(r => r.EmployeeId).Distinct().ToList();
        var punchIds = requests.Where(r => r.PunchId is not null).Select(r => r.PunchId!.Value).Distinct().ToList();

        var employees = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.ClientId == clientId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var punches = punchIds.Count == 0
            ? []
            : await db.Punches.IgnoreQueryFilters()
                .Where(p => p.ClientId == clientId && punchIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        return requests
            .Select(r => PunchChangeRequestResponse.FromEntity(
                r,
                employees.GetValueOrDefault(r.EmployeeId),
                r.PunchId is { } punchId ? punches.GetValueOrDefault(punchId) : null))
            .ToList();
    }

    /// <summary>Approve applies the change to the Punch table and writes the PunchAuditEntry; deny
    /// just records the decision — no audit entry, since nothing about any Punch changed
    /// (PunchChangeRequest's own doc comment on why that's not redundant with PunchAuditEntry).
    /// No "reviewer != requester" check — UI_PLAN.md's Phase 6 notes call that out as a stricter mode
    /// to leave room for later, not something to enforce now.</summary>
    public async Task<ServiceResult<PunchChangeRequest>> DecideAsync(
        int id, DecidePunchChangeRequestRequest request, string reviewerUserId, CancellationToken ct)
    {
        var changeRequest = await db.PunchChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (changeRequest is null)
        {
            return ServiceResult<PunchChangeRequest>.NotFound($"No punch change request with id {id}.");
        }

        if (changeRequest.Status != PunchChangeRequestStatus.Pending)
        {
            return ServiceResult<PunchChangeRequest>.Conflict(
                $"Punch change request {id} was already {changeRequest.Status}.");
        }

        if (request.Approve)
        {
            var applyError = await ApplyAsync(changeRequest, ct);
            if (applyError is not null)
            {
                return applyError;
            }

            changeRequest.Status = PunchChangeRequestStatus.Approved;
        }
        else
        {
            changeRequest.Status = PunchChangeRequestStatus.Denied;
        }

        changeRequest.ReviewerUserId = reviewerUserId;
        changeRequest.ReviewedAt = clock.GetCurrentInstant();
        changeRequest.ReviewNote = request.ReviewNote;

        await db.SaveChangesAsync(ct);

        return ServiceResult<PunchChangeRequest>.Success(changeRequest);
    }

    /// <summary>Applies an approved request to the Punch table and stages the corresponding
    /// PunchAuditEntry (added to the context, not yet saved — DecideAsync's own SaveChanges covers
    /// this alongside the request's own Status/Reviewer* fields). Returns null on success; a non-null
    /// result means applying failed and the caller should return that instead of marking the request
    /// Approved.</summary>
    private async Task<ServiceResult<PunchChangeRequest>?> ApplyAsync(PunchChangeRequest changeRequest, CancellationToken ct) =>
        changeRequest.ChangeKind switch
        {
            PunchChangeKind.Add => await ApplyAddAsync(changeRequest, ct),
            PunchChangeKind.Edit => await ApplyEditAsync(changeRequest, ct),
            PunchChangeKind.Delete => await ApplyDeleteAsync(changeRequest, ct),
            _ => throw new InvalidOperationException($"Unexpected {nameof(PunchChangeKind)} '{changeRequest.ChangeKind}'."),
        };

    private async Task<ServiceResult<PunchChangeRequest>?> ApplyAddAsync(PunchChangeRequest changeRequest, CancellationToken ct)
    {
        var punch = new Punch
        {
            ClientId = changeRequest.ClientId,
            EmployeeId = changeRequest.EmployeeId,
            PunchTime = changeRequest.RequestedPunchTime!.Value,
            PunchTimeZoneId = changeRequest.RequestedPunchTimeZoneId ?? "UTC",
            Kind = changeRequest.RequestedKind!.Value,
            Subtype = changeRequest.RequestedSubtype,
            PositionId = changeRequest.RequestedPositionId,
            Amount = changeRequest.RequestedAmount,
            Hours = changeRequest.RequestedHours,
            BonusKind = changeRequest.RequestedBonusKind,
            CountsTowardRegularRate = changeRequest.RequestedCountsTowardRegularRate ?? false,
            CreatedAt = clock.GetCurrentInstant(),
            CreatedBy = changeRequest.RequesterUserId,
        };

        db.Punches.Add(punch);
        // Needs its own SaveChanges — PunchAuditor.Created and the PunchId backfill below both need
        // the punch's real, DB-generated Id (same reasoning as PunchService.CreateAsync). The audit
        // entry and the PunchId/Status/Reviewer* updates on changeRequest itself still all land in
        // DecideAsync's own SaveChanges after this returns — only the punch insert needed its own.
        await db.SaveChangesAsync(ct);

        db.PunchAudits.Add(PunchAuditor.Created(punch, changeRequest.RequesterUserId, punch.CreatedAt));
        changeRequest.PunchId = punch.Id;

        return null;
    }

    private async Task<ServiceResult<PunchChangeRequest>?> ApplyEditAsync(PunchChangeRequest changeRequest, CancellationToken ct)
    {
        var existing = await db.Punches.FirstOrDefaultAsync(p => p.Id == changeRequest.PunchId!.Value, ct);
        if (existing is null)
        {
            return ServiceResult<PunchChangeRequest>.Conflict(
                $"Punch {changeRequest.PunchId} no longer exists — it may have been deleted since this request was submitted.");
        }

        // Reuses PunchRequestMapper/PunchRequestValidator — the exact merge-and-validate logic
        // PunchService.UpdateAsync runs for a direct PUT, just fed from the request's Requested*
        // fields instead of a caller-supplied UpdatePunchRequest.
        var updateRequest = new UpdatePunchRequest
        {
            PunchTime = changeRequest.RequestedPunchTime,
            PunchTimeZoneId = changeRequest.RequestedPunchTimeZoneId,
            Kind = changeRequest.RequestedKind,
            Subtype = changeRequest.RequestedSubtype,
            PositionId = changeRequest.RequestedPositionId,
            Amount = changeRequest.RequestedAmount,
            Hours = changeRequest.RequestedHours,
            BonusKind = changeRequest.RequestedBonusKind,
            CountsTowardRegularRate = changeRequest.RequestedCountsTowardRegularRate,
        };
        var updated = PunchRequestMapper.ApplyUpdate(existing, updateRequest);

        var errors = PunchRequestValidator.ValidateConsistency(updated);
        if (errors.Count > 0)
        {
            return ServiceResult<PunchChangeRequest>.ValidationFailed(errors);
        }

        // Built before SetValues below — same ordering requirement PunchService.UpdateAsync documents:
        // SetValues mutates existing's own backing fields in place, so it has to happen after the
        // "before" snapshot is captured, not before.
        var auditEntry = PunchAuditor.Edited(
            existing, updated, changeRequest.RequesterUserId, clock.GetCurrentInstant(), changeRequest.Reason);

        db.Entry(existing).CurrentValues.SetValues(updated);
        db.PunchAudits.Add(auditEntry);

        return null;
    }

    private async Task<ServiceResult<PunchChangeRequest>?> ApplyDeleteAsync(PunchChangeRequest changeRequest, CancellationToken ct)
    {
        var existing = await db.Punches.FirstOrDefaultAsync(p => p.Id == changeRequest.PunchId!.Value, ct);
        if (existing is null)
        {
            return ServiceResult<PunchChangeRequest>.Conflict(
                $"Punch {changeRequest.PunchId} no longer exists — it may already have been deleted.");
        }

        var deleted = existing with { IsDeleted = true };
        var auditEntry = PunchAuditor.Deleted(existing, changeRequest.RequesterUserId, clock.GetCurrentInstant(), changeRequest.Reason);

        db.Entry(existing).CurrentValues.SetValues(deleted);
        db.PunchAudits.Add(auditEntry);

        return null;
    }
}
