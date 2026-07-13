using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// End-to-end fixtures with hand-computed expected pay, exercising the whole pipeline for
/// representative jurisdictions and pay compositions.  These are the "recorded expected output"
/// tests: if a stage changes a number, one of these should move.
/// </summary>
public class RecordedScenarioTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };

    private Punch In(int day, int hour, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, day, hour, 0), PunchKind.In, _emp)
            with { PositionId = positionId };
    private Punch Out(int day, int hour, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, day, hour, 0), PunchKind.Out, _emp)
            with { PositionId = positionId };

    private static PipelineContext Ctx(PayRule rule, params Position[] positions)
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var assignments = positions
            .Select(p => new EmployeePositionAssignment(p, new LocalDate(2000, 1, 1)))
            .ToList();
        return new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], assignments);
    }

    [Fact]
    public void Federal_45HourWeek_SingleRate()
    {
        // Mon–Fri Jan 2–6, 9 hrs each = 45 hrs @ $20
        var punches = new List<Punch>();
        for (int d = 2; d <= 6; d++) { punches.Add(In(d, 8)); punches.Add(Out(d, 17)); }

        var pos = new Position { Id = 1, BaseRate = 20m };
        var result = PayCalculator.Calculate(punches, Ctx(new PayRule(), pos));

        // straight 45×20 = 900; OT premium 5 × 0.5 × 20 = 50 → 950
        Assert.Equal(950m, result.GrossPay);
        Assert.Equal(5m, result.Workweeks[0].OvertimeHours);
    }

    [Fact]
    public void California_TwelveHourDay_WithMissedMeal()
    {
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        rule.OvertimeRule.HasDailyOvertime = true;

        // Single 12-hr day, no lunch punched → daily OT + CA meal premium
        var punches = new List<Punch> { In(2, 6), Out(2, 18) };
        var pos = new Position { Id = 1, BaseRate = 20m };
        var result = PayCalculator.Calculate(punches, Ctx(rule, pos));

        // straight 12×20 = 240; OT premium 4 × 0.5 × 20 = 40; meal premium 1 × 20 = 20 → 300
        Assert.Equal(300m, result.GrossPay);
        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(4m, result.Workweeks[0].OvertimeHours);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 20m);
    }

    [Fact]
    public void EffectiveDatedRateChange_MidWeek_SplitsAndWeightsCorrectly()
    {
        // pos rate $20 through Jan 3, then $40 from Jan 4 — one shift can't span, but the week mixes rates.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var posEarly = new Position { Id = 1, BaseRate = 20m };
        var posLate = new Position { Id = 1, BaseRate = 40m };
        var ctx = new PipelineContext(
            emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [
                new EmployeePositionAssignment(posEarly, new LocalDate(2000, 1, 1), new LocalDate(2023, 1, 3)),
                new EmployeePositionAssignment(posLate, new LocalDate(2023, 1, 4)),
            ]);

        // Mon Jan 2 (8h @20) + Thu Jan 5 (8h @40) = 16 hrs, no OT
        var punches = new List<Punch> { In(2, 9), Out(2, 17), In(5, 9), Out(5, 17) };
        var result = PayCalculator.Calculate(punches, ctx);

        // straight = 8×20 + 8×40 = 160 + 320 = 480, no overtime
        Assert.Equal(480m, result.GrossPay);
        Assert.Equal(0m, result.Workweeks[0].OvertimeHours);
    }
}
