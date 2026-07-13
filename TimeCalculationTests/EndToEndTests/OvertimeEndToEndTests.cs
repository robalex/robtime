using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class OvertimeEndToEndTests : EndToEndTests
{
    [Fact]
    public void FederalOvertime_OverFortyHours_PaysHalfTimePremium()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 25m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 8))); punches.Add(Out(emp, At(d, 17))); }

        var result = PayCalculator.Calculate(punches, ctx);

        // 45 hrs @ $25 = 1125 straight; OT premium 5 × 0.5 × 25 = 62.5 → 1187.5
        Assert.Equal(1187.5m, result.GrossPay);
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(5m, result.Workweeks[0].OvertimeHours);
    }

    [Fact]
    public void California_FourteenHourDay_HasDoubletime()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { OvertimeRule = new OvertimeRule { HasDailyOvertime = true } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch> { In(emp, At(2, 6)), Out(emp, At(2, 20)) };   // 14 hrs

        var result = PayCalculator.Calculate(punches, ctx);

        // 8 reg + 4 OT (8-12) + 2 DT (>12): straight 14×20=280; OT 4×0.5×20=40; DT 2×1×20=40 → 360
        Assert.Equal(360m, result.GrossPay);
        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(4m, result.Workweeks[0].OvertimeHours);
        Assert.Equal(2m, result.Workweeks[0].DoubletimeHours);
    }

    [Fact]
    public void California_SevenConsecutiveDays_SeventhDayAndWeeklyCapBothApply()
    {
        // Sun Jan 1 - Sat Jan 7 2023, 8 hrs/day, single workweek (default anchor = Sunday).
        // Days 1-6 (applyDaily branch): 8 reg hrs each = 48 total before the weekly cap.
        // Day 7 (7th-consecutive-day branch): 8 hrs, all <= 8 -> entirely at the OT tier.
        // Weekly cap: 48 regular > 40 -> 8 more hours reclassified regular -> overtime.
        // Final: Regular=40, Overtime=8(day7)+8(weekly cap)=16, Doubletime=0.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { OvertimeRule = new OvertimeRule { HasDailyOvertime = true, HasSeventhDayRule = true } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        for (int d = 1; d <= 7; d++) { punches.Add(In(emp, At(d, 9))); punches.Add(Out(emp, At(d, 17))); }

        var result = PayCalculator.Calculate(punches, ctx);
        var week = result.Workweeks[0];

        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(16m, week.OvertimeHours);
        Assert.Equal(0m, week.DoubletimeHours);
        // straight 56×20=1120; RROP=1120/56=20; OT premium=16×0.5×20=160 → 1280
        Assert.Equal(1280m, result.GrossPay);
    }
}
