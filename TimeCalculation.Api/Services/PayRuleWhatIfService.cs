using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using TimeCalculation.Pipeline;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Phase 4 §7's single-employee what-if (UI_PLAN.md): runs PayCalculator twice over the same real
/// punches — once under whatever's actually assigned to the employee ("current"), once forcing the
/// route's PayRule to cover the whole period ("draft") — and diffs the results. The second sanctioned
/// caller of PayCalculator from the API, per TimeCalculation.Api.csproj's guard comment (the first is
/// GET /metadata/premium-rules, which only reads PremiumRegistry).
/// </summary>
public class PayRuleWhatIfService(PayrollDbContext db)
{
    public async Task<ServiceResult<WhatIfResponse>> RunAsync(int payRuleId, WhatIfRequest request, CancellationToken ct)
    {
        if (request.PeriodEnd <= request.PeriodStart)
        {
            return ServiceResult<WhatIfResponse>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["periodEnd"] = ["Period end must be after period start."],
            });
        }

        if (Period.Between(request.PeriodStart, request.PeriodEnd, PeriodUnits.Days).Days > 92)
        {
            return ServiceResult<WhatIfResponse>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["periodEnd"] = ["Period cannot span more than 92 days — pick a shorter window to preview."],
            });
        }

        var draftRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == payRuleId, ct);
        if (draftRule is null)
        {
            return ServiceResult<WhatIfResponse>.NotFound($"No pay rule with id {payRuleId}.");
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null)
        {
            return ServiceResult<WhatIfResponse>.NotFound($"No employee with id {request.EmployeeId}.");
        }

        var zone = DateTimeZoneProviders.Tzdb[employee.HomeTimeZoneId];
        var periodStartInstant = request.PeriodStart.AtStartOfDayInZone(zone).ToInstant();
        var periodEndInstant = request.PeriodEnd.AtStartOfDayInZone(zone).ToInstant();

        var punches = await db.Punches
            .Where(p => p.EmployeeId == request.EmployeeId
                && p.PunchTime >= periodStartInstant && p.PunchTime < periodEndInstant)
            .OrderBy(p => p.PunchTime)
            .ToListAsync(ct);

        // Full assignment history, not just what overlaps the period — PipelineContext.GetRuleAt/
        // GetRateAt resolve the applicable row per instant themselves (same as every real
        // calculation call site); pre-filtering here would just be redundant, error-prone work.
        var payRuleAssignments = await db.PayRuleAssignments
            .Include(a => a.PayRule)
            .Where(a => a.EmployeeId == request.EmployeeId)
            .ToListAsync(ct);
        var positionAssignments = await db.EmployeePositionAssignments
            .Include(a => a.Position)
            .Where(a => a.EmployeeId == request.EmployeeId)
            .ToListAsync(ct);
        var differentialRules = await db.DifferentialRules.ToListAsync(ct);
        // Real waiver overrides, not the empty-list default — otherwise a client's attested
        // ClientPremiumPolicy never actually affects a what-if run (see PipelineContext.
        // GetWaiverPolicyOverridesAt, which this list feeds). Scoped by the tenant query filter,
        // same as differentialRules above.
        var clientPremiumPolicies = await db.ClientPremiumPolicies.ToListAsync(ct);

        var currentAssignments = payRuleAssignments.Select(a => a.ToDomain()).ToList();
        var positionAssignmentsDomain = positionAssignments.Select(a => a.ToDomain()).ToList();

        var currentCtx = new PipelineContext(
            employee, currentAssignments, positionAssignmentsDomain, differentialRules,
            clientPremiumPolicies: clientPremiumPolicies);

        // Label for the "Current" summary — the rule in effect at the period's start. If the
        // employee's real assignments don't cover that instant (sparse history, or none at all),
        // there's nothing meaningful to call "current"; the response still needs a summary, so this
        // falls back to the draft rule's own identity rather than throwing on a labeling lookup that
        // isn't essential to the diff itself (the per-shift diff below is unaffected either way).
        // Resolved before Calculate, not just for the label — it also decides which HolidayCalendar
        // currentCtx runs with, below.
        var currentRuleFound = currentCtx.TryGetRuleAt(periodStartInstant, out var resolvedCurrentRule);
        var currentRule = currentRuleFound ? resolvedCurrentRule : draftRule;

        // Phase-one simplification (see PipelineContext.HolidayCalendar's own doc comment): one
        // calendar for the whole run, chosen from whichever PayRule is effective at the period's
        // start — not a per-instant resolver across an assignment history that might span multiple
        // PayRules with different calendars.
        if (currentRuleFound && resolvedCurrentRule.HolidayCalendarId is { } currentHolidayCalendarId)
        {
            var currentHolidayCalendar = await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == currentHolidayCalendarId, ct);
            currentCtx = new PipelineContext(
                employee, currentAssignments, positionAssignmentsDomain, differentialRules,
                currentHolidayCalendar, clientPremiumPolicies);
        }

        var draftHolidayCalendar = draftRule.HolidayCalendarId is { } draftHolidayCalendarId
            ? await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == draftHolidayCalendarId, ct)
            : null;

        // The synthetic assignment must cover more than [PeriodStart, PeriodEnd): PayCalculator
        // resolves the overtime rule from each *workweek's* start instant (PayCalculator.cs,
        // CalculateWorkweekPay), which for a punch near PeriodStart can fall up to 6 days earlier
        // (e.g. a Monday shift's FLSA week starting the preceding Sunday). Padding a week on each
        // side guarantees every workweek touching the period is fully inside the window, whatever
        // WorkweekStartDay the draft rule uses.
        var draftCtx = new PipelineContext(
            employee,
            [new PayRuleAssignment(draftRule, request.PeriodStart.PlusDays(-7), request.PeriodEnd.PlusDays(7))],
            positionAssignmentsDomain,
            differentialRules,
            draftHolidayCalendar,
            clientPremiumPolicies);

        PayResult currentResult;
        try
        {
            currentResult = PayCalculator.Calculate(punches, currentCtx);
        }
        catch (InvalidOperationException ex)
        {
            // GetRuleAt's own gap message (see PipelineContext) — surfaced as a conflict rather than
            // a 500, since it means the employee's real assignment history doesn't cover this period,
            // not that anything about the request itself was malformed.
            return ServiceResult<WhatIfResponse>.Conflict(
                $"Could not calculate current pay for this period: {ex.Message}");
        }

        // The synthetic single-assignment draftCtx always fully covers [PeriodStart, PeriodEnd), so
        // this can't throw the same way.
        var draftResult = PayCalculator.Calculate(punches, draftCtx);

        var response = WhatIfDiffBuilder.Build(request, currentRule, currentResult, draftRule, draftResult);
        return ServiceResult<WhatIfResponse>.Success(response);
    }
}
