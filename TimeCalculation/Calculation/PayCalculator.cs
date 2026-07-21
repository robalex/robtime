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
        var shifts = PrepareShifts(punches, ctx);

        // Differentials must run before grouping so the regular rate includes them (Stage 8).
        var shiftsWithDifferentials = DifferentialApplier.Execute(shifts, ctx);

        // Stage 8b: a consecutive-range differential with a MinHoursInRange threshold can only be
        // judged once the whole range occurrence is visible (independent of the payroll week), so
        // strip non-qualifying ones here before the regular rate reads them.
        var shiftsQualified = RangeDifferentialQualifier.Execute(shiftsWithDifferentials, ctx);

        var weekPays = CalculateWeeklyPay(shiftsQualified, ctx, overridesForShift);

        return new PayResult { EmployeeId = ctx.Employee.Id, Workweeks = weekPays };
    }

    /// <summary>Stages 1–6: raw punches → rounded, paired, enriched, built, subtyped, and dated shifts.</summary>
    private static IReadOnlyList<Shift> PrepareShifts(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var rounded = PunchRounder.RoundPunches(punches, ctx);
        var (pairs, fixedEntries) = PunchPairer.PairPunches(rounded, ctx);
        var enriched = PairPositionAndRateAttacher.AttachPositionAndRateToPunchPairs(pairs, ctx);
        var shifts = ShiftBuilder.BuildShifts(enriched, fixedEntries, ctx);
        var subtyped = PunchSubtypeInferrer.InferPunchSubtypes(shifts, ctx);
        return ShiftDater.AssignDatesToShifts(subtyped, ctx);
    }

    /// <summary>Stages 9–13: group shifts into workweeks, then compute each week's pay.</summary>
    private static IReadOnlyList<WorkweekPay> CalculateWeeklyPay(
        IReadOnlyList<Shift> shifts,
        PipelineContext ctx,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift)
    {
        var days = WorkDayGrouper.Execute(shifts, ctx);
        var weeks = WorkweekGrouper.Execute(days, ctx);

        return weeks
            .Select(week => CalculateWorkweekPay(week, ctx, overridesForShift))
            .ToList();
    }

    /// <summary>
    /// One workweek: regular rate (Stage 11) → overtime (Stage 12) → premiums (Stage 7, priced at
    /// that rate) → summarize (Stage 13).  Premiums come after the regular rate but do not feed it,
    /// so there is no circular dependency.
    /// </summary>
    private static WorkweekPay CalculateWorkweekPay(
        Workweek week,
        PipelineContext ctx,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift)
    {
        var regularRate = RegularRateCalculator.Calculate(week, ctx.Employee.MinimumWage);

        var overtimeRule = OvertimeRuleFactory.FromConfig(ctx.GetRuleAt(week.StartInstant).OvertimeRule);
        var overtime = OvertimeCalculator.Calculate(week, overtimeRule, regularRate.RegularRate);

        var weekShifts = week.Days.SelectMany(d => d.Shifts).ToList();
        var shiftsWithPremiums = PremiumApplier.ApplyPremiums(
            weekShifts, ctx, _ => regularRate.RegularRate, overridesForShift);

        return PaySummarizer.Summarize(
            week, shiftsWithPremiums, regularRate, overtime, ctx.Employee.MinimumWage);
    }
}
