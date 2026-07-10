using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;

namespace TimeCalculation.Calculation;

/// <summary>
/// End-to-end orchestrator.  Runs the pure pipeline stages in order and produces a PayResult.
/// It is a coordinator, not a calculator — each stage remains its own single-responsibility unit.
///
/// Ordering note: premiums (nominal Stage 7) are applied after the regular rate is known (Stage 11)
/// because a premium is "one hour at the regular rate".  This is not circular: the regular rate is
/// computed from earnings that exclude premiums.
///
/// Deterministic: the same punches + context always produce an equal PayResult (see idempotency tests).
/// </summary>
public static class PayCalculator
{
    public static PayResult Calculate(
        IReadOnlyList<Punch> punches,
        PipelineContext ctx,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift = null)
    {
        // Stages 1–6: punches → dated shifts
        var rounded = PunchRounder.Execute(punches, ctx);
        var subtyped = PunchSubtypeInferrer.Execute(rounded, ctx);
        var (pairs, fixedEntries) = PunchPairer.Execute(subtyped, ctx);
        var enriched = PairEnricher.Execute(pairs, ctx);
        var shifts = ShiftBuilder.Execute(enriched, fixedEntries, ctx);
        var dated = ShiftDater.Execute(shifts, ctx);

        // Stage 8: differentials (needed before grouping so the regular rate sees them)
        var withDiffs = DifferentialApplier.Execute(dated, ctx);

        // Stages 9–10: group into days then workweeks
        var days = WorkDayGrouper.Execute(withDiffs, ctx);
        var weeks = WorkweekGrouper.Execute(days, ctx);

        var weekPays = new List<WorkweekPay>(weeks.Count);
        foreach (var week in weeks)
        {
            // Stage 11: regular rate of pay
            var regularRate = RegularRateCalculator.Calculate(week);

            // Stage 12: overtime, using the rule configured on the PayRule active that week
            var otRule = OvertimeRuleFactory.FromConfig(ctx.GetRuleAt(week.StartInstant).OvertimeRule);
            var overtime = OvertimeCalculator.Calculate(week, otRule, regularRate.RegularRate);

            // Stage 7: premiums per shift, priced at this week's regular rate
            var weekShifts = week.Days.SelectMany(d => d.Shifts).ToList();
            var withPremiums = PremiumApplier.Execute(
                weekShifts, ctx, _ => regularRate.RegularRate, overridesForShift);

            // Stage 13: summarize
            weekPays.Add(PaySummarizer.Summarize(
                week, withPremiums, regularRate, overtime, ctx.Employee.MinimumWage));
        }

        return new PayResult { EmployeeId = ctx.Employee.Id, Workweeks = weekPays };
    }
}
