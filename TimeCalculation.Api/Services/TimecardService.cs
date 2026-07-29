using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;
using TimeCalculation.Pipeline;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Runs PayCalculator for one employee's actual current pay period — the third sanctioned engine
/// caller from the API, per TimeCalculation.Api.csproj's guard comment (the first two are
/// GET /metadata/premium-rules and PayRuleWhatIfService). Also owns approving/un-approving a period
/// (Phase 6.7, UI_PLAN.md decision 21): approval is "run the same calculation once more, freeze it,
/// and remember that it happened" — the resolve-period-and-run-the-engine work is identical to a plain
/// read, so ApproveAsync shares GetAsync's ResolvePeriodAsync rather than re-deriving period bounds.
/// </summary>
public class TimecardService(PayrollDbContext db, IClock clock)
{
    // Identifies which build of the engine produced a snapshot (PayCalculationSnapshot.EngineVersion)
    // — a GUID unique per compiled build, not a semantic version, computed once since it can't change
    // within a running process.
    private static readonly string EngineVersion =
        typeof(PayCalculator).Assembly.ManifestModule.ModuleVersionId.ToString();

    public async Task<ServiceResult<TimecardResponse>> GetAsync(
        int employeeId, LocalDate? date, bool callerIsEmployeeRole, CancellationToken ct)
    {
        var resolved = await ResolvePeriodAsync(employeeId, date, ct);
        if (resolved.Kind is not ServiceResultKind.Success)
        {
            return Remap<PeriodResolution, TimecardResponse>(resolved);
        }

        var (employee, rule, period, ctx, periodStartInstant, periodEndInstant) = resolved.Value!;
        var showFullItemization = !callerIsEmployeeRole || await ShowsFullItemizationAsync(employee.ClientId, ct);

        // An approved period renders from its frozen snapshot, never re-running the engine — decision
        // 24: locking the punches (below) stops the *inputs* from changing, but only the snapshot
        // stops an engine/config change from silently moving the *output* out from under a period
        // someone already signed off on.
        var approval = await ActiveApprovalAsync(employeeId, period, ct);
        if (approval is not null)
        {
            var snapshot = TimecardSnapshotSerializer.Deserialize(approval.SnapshotJson);
            var detail = new PayCalculationDetail { Result = snapshot.Result, Weeks = snapshot.Weeks };
            var frozen = TimecardResponse.FromDomain(
                detail, snapshot.PayRuleId, snapshot.PayRuleName, snapshot.PayRuleVersion,
                rule.WorkweekStartDay, period.Start, period.End, showFullItemization);
            return ServiceResult<TimecardResponse>.Success(frozen with
            {
                IsLocked = true,
                ApprovedByUserId = approval.ApprovedByUserId,
                ApprovedAt = approval.ApprovedAt,
            });
        }

        var liveDetail = await CalculateAsync(employeeId, periodStartInstant, periodEndInstant, ctx, ct);
        if (liveDetail.Kind is not ServiceResultKind.Success)
        {
            return Remap<PayCalculationDetail, TimecardResponse>(liveDetail);
        }

        var response = TimecardResponse.FromDomain(
            liveDetail.Value!, rule.Id, rule.Name, rule.Version, rule.WorkweekStartDay,
            period.Start, period.End, showFullItemization);
        return ServiceResult<TimecardResponse>.Success(response);
    }

    /// <summary>
    /// Freezes the period's current calculation into a TimecardApproval row — UI_PLAN.md decision 21.
    /// Refused (Conflict) if the period is already approved or any PunchChangeRequest against it is
    /// still Pending (decision 23: a supervisor must decide those first, not have them silently swept
    /// aside by approval). Always full itemization: only Supervisor+ reaches this endpoint.
    /// </summary>
    public async Task<ServiceResult<TimecardResponse>> ApproveAsync(
        int employeeId, LocalDate? date, string approverUserId, CancellationToken ct)
    {
        var resolved = await ResolvePeriodAsync(employeeId, date, ct);
        if (resolved.Kind is not ServiceResultKind.Success)
        {
            return Remap<PeriodResolution, TimecardResponse>(resolved);
        }

        var (employee, rule, period, ctx, periodStartInstant, periodEndInstant) = resolved.Value!;

        var alreadyApproved = await ActiveApprovalAsync(employeeId, period, ct);
        if (alreadyApproved is not null)
        {
            return ServiceResult<TimecardResponse>.Conflict(
                $"This timecard is already approved (by {alreadyApproved.ApprovedByUserId}).");
        }

        var pendingCount = await CountPendingRequestsInPeriodAsync(employeeId, periodStartInstant, periodEndInstant, ct);
        if (pendingCount > 0)
        {
            return ServiceResult<TimecardResponse>.Conflict(
                pendingCount == 1
                    ? "1 punch change request is still pending for this period. Decide it before approving."
                    : $"{pendingCount} punch change requests are still pending for this period. Decide them before approving.");
        }

        var detailResult = await CalculateAsync(employeeId, periodStartInstant, periodEndInstant, ctx, ct);
        if (detailResult.Kind is not ServiceResultKind.Success)
        {
            return Remap<PayCalculationDetail, TimecardResponse>(detailResult);
        }

        var detail = detailResult.Value!;
        var snapshot = new PayCalculationSnapshot
        {
            EmployeeId = employeeId,
            ClientId = employee.ClientId,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            CalculatedAt = clock.GetCurrentInstant(),
            EngineVersion = EngineVersion,
            PayRuleId = rule.Id,
            PayRuleName = rule.Name,
            PayRuleVersion = rule.Version,
            Result = detail.Result,
            Weeks = StripAppliedRuleForSnapshot(detail.Weeks),
        };

        var approval = new TimecardApproval
        {
            ClientId = employee.ClientId,
            EmployeeId = employeeId,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            ApprovedByUserId = approverUserId,
            ApprovedAt = snapshot.CalculatedAt,
            SnapshotJson = TimecardSnapshotSerializer.Serialize(snapshot),
        };
        db.TimecardApprovals.Add(approval);
        await db.SaveChangesAsync(ct);

        var response = TimecardResponse.FromDomain(
            detail, rule.Id, rule.Name, rule.Version, rule.WorkweekStartDay,
            period.Start, period.End, showFullItemization: true);
        return ServiceResult<TimecardResponse>.Success(response with
        {
            IsLocked = true,
            ApprovedByUserId = approval.ApprovedByUserId,
            ApprovedAt = approval.ApprovedAt,
        });
    }

    /// <summary>Closes out the active approval row (UnapprovedByUserId/UnapprovedAt) rather than
    /// deleting it — the row itself is the audit record of who unlocked it and when, per
    /// TimecardApproval's own append-only design. Returns the now-live view by delegating back to
    /// GetAsync rather than duplicating its calculation, matching this class's own reuse pattern.
    /// </summary>
    public async Task<ServiceResult<TimecardResponse>> UnapproveAsync(
        int employeeId, LocalDate? date, string actorUserId, CancellationToken ct)
    {
        var resolved = await ResolvePeriodAsync(employeeId, date, ct);
        if (resolved.Kind is not ServiceResultKind.Success)
        {
            return Remap<PeriodResolution, TimecardResponse>(resolved);
        }

        var period = resolved.Value!.Period;
        var approval = await ActiveApprovalAsync(employeeId, period, ct);
        if (approval is null)
        {
            return ServiceResult<TimecardResponse>.Conflict("This timecard is not currently approved.");
        }

        approval.UnapprovedByUserId = actorUserId;
        approval.UnapprovedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);

        return await GetAsync(employeeId, date, callerIsEmployeeRole: false, ct);
    }

    /// <summary>
    /// Phase 6.8's bulk-entry grid: a running total for the period's real, already-saved punches with
    /// the grid's current draft rows merged in — not the draft rows in isolation, so what a supervisor
    /// sees while typing is what the period would actually total, missed punches and all. Nothing here
    /// is persisted; draft entries get synthetic negative ids (never a real Punch id, and distinct
    /// from each other) purely so Shift.AnchorPunchId — which falls back to a shared 0 only when no
    /// punch in a shift has a real id — doesn't collide two different draft-only shifts onto the same
    /// anchor and merge their line items in the response. This never matters for the merged calculation
    /// itself, only for a caller keying results by AnchorPunchId, but is cheap to get right regardless.
    /// Uses Calculate, not CalculateDetailed: a running total needs PayResult only, none of the
    /// punch-level grouping detail the full timecard view renders.
    /// </summary>
    public async Task<ServiceResult<BulkPunchPreviewResponse>> PreviewAsync(
        int employeeId, LocalDate? date, List<DraftPunchEntry> draftPunches, CancellationToken ct)
    {
        var resolved = await ResolvePeriodAsync(employeeId, date, ct);
        if (resolved.Kind is not ServiceResultKind.Success)
        {
            return Remap<PeriodResolution, BulkPunchPreviewResponse>(resolved);
        }

        var (employee, _, _, ctx, periodStartInstant, periodEndInstant) = resolved.Value!;

        var realPunches = await db.Punches
            .Where(p => p.EmployeeId == employeeId && p.PunchTime >= periodStartInstant && p.PunchTime < periodEndInstant)
            .ToListAsync(ct);

        var draftAsPunches = draftPunches.Select((entry, i) => new Punch
        {
            Id = -(i + 1),
            ClientId = employee.ClientId,
            EmployeeId = employeeId,
            PunchTime = entry.PunchTime,
            Kind = entry.Kind,
            Subtype = entry.Subtype,
            PositionId = entry.PositionId,
            Amount = entry.Amount,
            Hours = entry.Hours,
            BonusKind = entry.BonusKind,
            CountsTowardRegularRate = entry.CountsTowardRegularRate,
        });

        var merged = realPunches.Concat(draftAsPunches).OrderBy(p => p.PunchTime).ToList();

        PayResult result;
        try
        {
            result = PayCalculator.Calculate(merged, ctx);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<BulkPunchPreviewResponse>.Conflict($"Could not preview pay for this period: {ex.Message}");
        }

        return ServiceResult<BulkPunchPreviewResponse>.Success(new BulkPunchPreviewResponse
        {
            GrossPay = result.GrossPay,
            Weeks = result.Workweeks.Select(PreviewWeekSummary.FromDomain).ToList(),
        });
    }

    /// <summary>Everything both a plain read and an approval need: the employee, the PayRule in
    /// effect, the resolved pay period, and a ready-to-run PipelineContext. Kept as one method so
    /// GetAsync/ApproveAsync/UnapproveAsync can never resolve the "same" period two different ways.
    /// </summary>
    private async Task<ServiceResult<PeriodResolution>> ResolvePeriodAsync(
        int employeeId, LocalDate? date, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return ServiceResult<PeriodResolution>.NotFound($"No employee with id {employeeId}.");
        }

        var zone = DateTimeZoneProviders.Tzdb[employee.HomeTimeZoneId];
        var referenceDate = date ?? clock.GetCurrentInstant().InZone(zone).Date;

        var payRuleAssignments = await db.PayRuleAssignments
            .Include(a => a.PayRule)
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync(ct);
        var positionAssignments = await db.EmployeePositionAssignments
            .Include(a => a.Position)
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync(ct);
        var differentialRules = await db.DifferentialRules.ToListAsync(ct);
        var clientPremiumPolicies = await db.ClientPremiumPolicies.ToListAsync(ct);

        var currentAssignments = payRuleAssignments.Select(a => a.ToDomain()).ToList();
        var positionAssignmentsDomain = positionAssignments.Select(a => a.ToDomain()).ToList();

        var ctx = new PipelineContext(
            employee, currentAssignments, positionAssignmentsDomain, differentialRules,
            clientPremiumPolicies: clientPremiumPolicies);

        var referenceInstant = referenceDate.AtStartOfDayInZone(zone).ToInstant();
        if (!ctx.TryGetRuleAt(referenceInstant, out var effectiveRule))
        {
            return ServiceResult<PeriodResolution>.Conflict(
                $"No PayRule assignment covers {referenceDate} for employee {employeeId}.");
        }

        var period = PayPeriodCalculator.ContainingDate(
            effectiveRule.PayPeriodFrequency, referenceDate, effectiveRule.PayPeriodAnchor);

        var periodStartInstant = period.Start.AtStartOfDayInZone(zone).ToInstant();
        // End is inclusive (PayPeriod's own convention) — downstream range queries need an exclusive
        // upper bound, so this is "the start of the day after."
        var periodEndInstant = period.End.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        return ServiceResult<PeriodResolution>.Success(
            new PeriodResolution(employee, effectiveRule, period, ctx, periodStartInstant, periodEndInstant));
    }

    private async Task<ServiceResult<PayCalculationDetail>> CalculateAsync(
        int employeeId, Instant periodStartInstant, Instant periodEndInstant, PipelineContext ctx, CancellationToken ct)
    {
        var punches = await db.Punches
            .Where(p => p.EmployeeId == employeeId && p.PunchTime >= periodStartInstant && p.PunchTime < periodEndInstant)
            .OrderBy(p => p.PunchTime)
            .ToListAsync(ct);

        // CalculateDetailed, not Calculate: the timecard renders punch-level detail (in/out, raw vs
        // rounded, Break/Lunch, incomplete pairs) that a PayResult alone doesn't carry and that can't
        // be recovered by re-joining Punch rows afterwards — see PayCalculationDetail.
        try
        {
            return ServiceResult<PayCalculationDetail>.Success(PayCalculator.CalculateDetailed(punches, ctx));
        }
        catch (InvalidOperationException ex)
        {
            // GetRuleAt's own gap message — a punch elsewhere in the period fell outside assignment
            // coverage even though the reference date itself resolved fine. Same handling
            // PayRuleWhatIfService applies to the same exception.
            return ServiceResult<PayCalculationDetail>.Conflict($"Could not calculate pay for this period: {ex.Message}");
        }
    }

    /// <summary>
    /// Nulls PunchPair.AppliedRule throughout the grouping graph before it's frozen into a snapshot.
    /// AppliedRule carries the *full* PayRule object — including RoundingRule/OvertimeRule and the
    /// ActivePremiumCodes/ActiveDifferentialCodes IReadOnlySet<string> properties, which System.Text.Json
    /// cannot instantiate on deserialize (there's no concrete type to construct for an interface). It's
    /// also simply redundant: TimecardPunchPairResponse.FromDomain never reads it, and the snapshot
    /// already carries the rule's identity once, at the top level (PayRuleId/Name/Version) — repeating
    /// the entire config object on every single punch pair would bloat every snapshot for data nothing
    /// reads back. Same instinct as PunchAuditor nulling Employee/Position before its own snapshots.
    /// </summary>
    private static IReadOnlyList<Workweek> StripAppliedRuleForSnapshot(IReadOnlyList<Workweek> weeks) =>
        weeks.Select(week => week with
        {
            Days = week.Days.Select(day => day with
            {
                Shifts = day.Shifts.Select(shift => shift with
                {
                    PunchPairs = shift.PunchPairs.Select(pair => pair with { AppliedRule = null }).ToList(),
                }).ToList(),
            }).ToList(),
        }).ToList();

    /// <summary>The active (not-yet-unapproved) approval for this exact period, if any — "active"
    /// meaning the latest row for (EmployeeId, PeriodStart, PeriodEnd) still has UnapprovedAt == null,
    /// per TimecardApproval's append-only design (there is at most one such row at a time; a prior
    /// approve/unapprove cycle for the same period left its row with UnapprovedAt set, so it's already
    /// excluded here without needing an explicit "latest" ordering).</summary>
    private async Task<TimecardApproval?> ActiveApprovalAsync(int employeeId, PayPeriod period, CancellationToken ct) =>
        await db.TimecardApprovals.FirstOrDefaultAsync(
            a => a.EmployeeId == employeeId && a.UnapprovedAt == null
                 && a.PeriodStart == period.Start && a.PeriodEnd == period.End, ct);

    /// <summary>How many Pending PunchChangeRequests fall inside [periodStartInstant, periodEndInstant)
    /// — decision 23's approval gate. A request's "date" is the current punch it targets (Edit/Delete)
    /// or the requested new punch time (Add, which has no existing punch yet); an Edit's *requested*
    /// new time is deliberately not what's checked, since the request still belongs to the period its
    /// target punch is in today.</summary>
    private async Task<int> CountPendingRequestsInPeriodAsync(
        int employeeId, Instant periodStartInstant, Instant periodEndInstant, CancellationToken ct)
    {
        var pending = await db.PunchChangeRequests
            .Where(r => r.EmployeeId == employeeId && r.Status == PunchChangeRequestStatus.Pending)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        var punchIds = pending.Where(r => r.PunchId is not null).Select(r => r.PunchId!.Value).Distinct().ToList();
        var punchTimes = punchIds.Count == 0
            ? new Dictionary<int, Instant>()
            : await db.Punches.Where(p => punchIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PunchTime, ct);

        return pending.Count(r =>
        {
            var time = r.ChangeKind == PunchChangeKind.Add
                ? r.RequestedPunchTime
                : r.PunchId is { } punchId && punchTimes.TryGetValue(punchId, out var t) ? t : (Instant?)null;
            return time is { } instant && instant >= periodStartInstant && instant < periodEndInstant;
        });
    }

    private async Task<bool> ShowsFullItemizationAsync(int clientId, CancellationToken ct)
    {
        var settings = await db.ClientSettings.FirstOrDefaultAsync(s => s.ClientId == clientId, ct);
        return settings?.ShowFullPayItemizationToEmployees ?? false;
    }

    /// <summary>Carries a non-Success ServiceResult across a type change — Conflict/NotFound/
    /// ValidationFailed already have everything a caller needs (Detail/ValidationErrors), so this is
    /// just re-wrapping, never called with a Success result (see the InvalidOperationException below).
    /// </summary>
    private static ServiceResult<TOut> Remap<TIn, TOut>(ServiceResult<TIn> result) => result.Kind switch
    {
        ServiceResultKind.NotFound => ServiceResult<TOut>.NotFound(result.Detail!),
        ServiceResultKind.Conflict => ServiceResult<TOut>.Conflict(result.Detail!),
        ServiceResultKind.ValidationFailed => ServiceResult<TOut>.ValidationFailed(result.ValidationErrors!),
        ServiceResultKind.Forbidden => ServiceResult<TOut>.Forbidden(result.Detail!),
        _ => throw new InvalidOperationException($"Remap called with an unexpected {nameof(ServiceResultKind)} '{result.Kind}'."),
    };

    private sealed record PeriodResolution(
        Employee Employee, PayRule Rule, PayPeriod Period, PipelineContext Context,
        Instant PeriodStartInstant, Instant PeriodEndInstant);
}
