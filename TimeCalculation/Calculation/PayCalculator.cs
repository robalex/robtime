using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;
using TimeCalculation.Pipeline.ShiftBuilding;

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
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift = null) =>
        CalculateDetailed(punches, ctx, overridesForShift).Result;

    /// <summary>
    /// The same calculation, additionally returning the Workweek → WorkDay → Shift → PunchPair graph
    /// it grouped along the way. For callers that must render or freeze punch-level detail (the
    /// timecard, and the approval snapshot behind it) — see PayCalculationDetail for why that detail
    /// can't be reconstructed from a PayResult after the fact. Calculate() is this minus the graph,
    /// so the two can never disagree about the pay they report.
    /// </summary>
    public static PayCalculationDetail CalculateDetailed(
        IReadOnlyList<Punch> punches,
        PipelineContext ctx,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift = null)
    {
        var shifts = PrepareShifts(punches, ctx);

        // Differentials must run before grouping so the regular rate includes them (Stage 8).
        var shiftsWithDifferentials = DifferentialApplier.ApplyDifferentials(shifts, ctx);

        // Stage 8b: a consecutive-range differential with a MinHoursInRange threshold can only be
        // judged once the whole range occurrence is visible (independent of the payroll week), so
        // strip non-qualifying ones here before the regular rate reads them.
        var shiftsQualified = RangeDifferentialQualifier.Execute(shiftsWithDifferentials, ctx);

        // Stages 9–10. Held in a local rather than folded into the pay projection below because the
        // grouping itself is half of what this method returns.
        var days = WorkDayGrouper.Execute(shiftsQualified, ctx);
        var weeks = WorkweekGrouper.Execute(days, ctx);

        var weekPays = weeks
            .Select(week => CalculateWorkweekPay(week, ctx, overridesForShift))
            .ToList();

        return new PayCalculationDetail
        {
            Result = new PayResult { EmployeeId = ctx.Employee.Id, Workweeks = weekPays },
            Weeks = weeks,
        };
    }

    /// <summary>Stages 1–6: raw punches → rounded, paired, enriched, built, subtyped, and dated shifts.
    /// internal (not private): DifferentialExplainer runs this same preparation to get the shift list
    /// differentials are evaluated against, without re-listing the stage sequence itself.</summary>
    internal static IReadOnlyList<Shift> PrepareShifts(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var rounded = PunchRounder.RoundPunches(punches, ctx);
        var (pairs, fixedEntries) = PunchPairer.PairPunches(rounded, ctx);
        var enriched = PairPositionAndRateAttacher.AttachPositionAndRateToPunchPairs(pairs, ctx);
        var shifts = ShiftBuilder.BuildShifts(enriched, fixedEntries, ctx);
        var subtyped = PunchSubtypeInferrer.InferPunchSubtypes(shifts, ctx);
        return ShiftDater.AssignDatesToShifts(subtyped, ctx);
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
