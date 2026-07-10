using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PayCalculatorTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };

    // Position paying $20 so straight-time math is round.
    private static PipelineContext Context(PayRule? rule = null)
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var pos = new Position { Id = 5, BaseRate = 20m, Name = "Line" };
        return new PipelineContext(
            emp,
            [new PayRuleAssignment(rule ?? new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(pos, new LocalDate(2000, 1, 1))]);
    }

    // Builds In/Out punches for `days` consecutive days starting Mon Jan 2 2023, each `hours` long from 09:00.
    private List<Punch> Week(int days, int hours)
    {
        var punches = new List<Punch>();
        for (int d = 0; d < days; d++)
        {
            var start = Instant.FromUtc(2023, 1, 2 + d, 9, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(hours), PunchKind.Out, _emp));
        }
        return punches;
    }

    [Fact]
    public void FortyHourWeek_NoOvertime_GrossIsStraightTime()
    {
        var result = PayCalculator.Calculate(Week(5, 8), Context());

        Assert.Single(result.Workweeks);
        Assert.Equal(800m, result.GrossPay);                 // 40 × $20
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(0m, result.Workweeks[0].OvertimeHours);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Regular && l.Amount == 800m);
        Assert.DoesNotContain(result.LineItems, l => l.Type == PayLineType.OvertimePremium);
    }

    [Fact]
    public void FiftyHourWeek_Federal_TenHoursOvertimePremium()
    {
        var result = PayCalculator.Calculate(Week(5, 10), Context());

        // straight 50 × 20 = 1000, OT premium 10 × 0.5 × 20 = 100 → 1100
        Assert.Equal(1100m, result.GrossPay);
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(10m, result.Workweeks[0].OvertimeHours);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.OvertimePremium && l.Amount == 100m);
    }

    [Fact]
    public void California_TwelveHourDay_ProducesDailyOvertime()
    {
        var rule = new PayRule();
        rule.OvertimeRule.HasDailyOvertime = true;
        // one 12-hour day: 8 reg + 4 OT
        var result = PayCalculator.Calculate(Week(1, 12), Context(rule));

        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(4m, result.Workweeks[0].OvertimeHours);
        // straight 12 × 20 = 240, OT premium 4 × 0.5 × 20 = 40 → 280
        Assert.Equal(280m, result.GrossPay);
    }

    [Fact]
    public void Idempotent_SameInputsProduceEqualLineItems()
    {
        var a = PayCalculator.Calculate(Week(5, 10), Context());
        var b = PayCalculator.Calculate(Week(5, 10), Context());

        Assert.Equal(a.GrossPay, b.GrossPay);
        Assert.Equal(a.LineItems, b.LineItems);   // PayLineItem is a scalar record → structural equality
    }

    [Fact]
    public void EmptyPunches_ProduceEmptyResult()
    {
        var result = PayCalculator.Calculate([], Context());
        Assert.Empty(result.Workweeks);
        Assert.Equal(0m, result.GrossPay);
    }

    [Fact]
    public void SplitAcrossTwoWorkweeks_ProducesTwoWorkweekPays()
    {
        // Fri/Sat (Jan 6/7) in week of Jan 1; Sun/Mon (Jan 8/9) in week of Jan 8
        var punches = new List<Punch>();
        foreach (var day in new[] { 6, 7, 8, 9 })
        {
            var start = Instant.FromUtc(2023, 1, day, 9, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(8), PunchKind.Out, _emp));
        }

        var result = PayCalculator.Calculate(punches, Context());
        Assert.Equal(2, result.Workweeks.Count);
        Assert.Equal(4 * 8 * 20m, result.GrossPay);
    }
}
