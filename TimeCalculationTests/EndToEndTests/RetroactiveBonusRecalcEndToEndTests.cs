
using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class RetroactiveBonusRecalcEndToEndTests : EndToEndTests
{
    [Fact]
    public void RetroactiveBonus_AcrossTwoRealWorkweeks_DerivedFromRawPunches()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        foreach (int startDay in new[] { 2, 9 })   // Mon Jan 2 (week of Jan 1) and Mon Jan 9 (week of Jan 8)
        {
            for (int d = startDay; d < startDay + 5; d++)
            {
                punches.Add(In(emp, At(d, 8)));
                punches.Add(Out(emp, At(d, 18)));   // 10 hrs/day × 5 = 50 hrs/week
            }
        }

        var weeks = BuildWeeks(punches, ctx);
        Assert.Equal(2, weeks.Count);

        var result = RetroactiveBonusRecalculator.Recalculate(400m, weeks, new FederalOvertimeRule(), emp.MinimumWage);

        // Each week: 50 hrs (40 reg + 10 OT), equal hours -> $200 bonus/week.
        // Rate increase = 200/50 = $4/hr; additional OT premium = 10×0.5×4 = $20/week -> $40 total.
        Assert.Equal(2, result.PerWeek.Count);
        Assert.All(result.PerWeek, w => Assert.Equal(200m, w.AllocatedBonus));
        Assert.All(result.PerWeek, w => Assert.Equal(20m, w.AdditionalOvertimePremium));
        Assert.Equal(40m, result.AdditionalOvertimePremium);
    }

    // Mirrors PayCalculator.PrepareShifts + CalculateWeeklyPay's grouping, stopping short of pay
    // calculation, to get the real Workweek objects the pipeline produces from raw punches.
    private static IReadOnlyList<Workweek> BuildWeeks(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var rounded = PunchRounder.RoundPunches(punches, ctx);
        var (pairs, fixedEntries) = PunchPairer.PairPunches(rounded, ctx);
        var enriched = PairPositionAndRateAttacher.AttachPositionAndRateToPunchPairs(pairs, ctx);
        var shifts = ShiftBuilder.BuildShifts(enriched, fixedEntries, ctx);
        var subtyped = PunchSubtypeInferrer.InferPunchSubtypes(shifts, ctx);
        var dated = ShiftDater.AssignDatesToShifts(subtyped, ctx);
        var days = WorkDayGrouper.Execute(dated, ctx);
        return WorkweekGrouper.Execute(days, ctx);
    }
}
