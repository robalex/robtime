using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class ShiftDatingEndToEndTests : EndToEndTests
{
    private static PipelineContext Ctx(Employee emp, ShiftDateStrategy strategy)
        => new(emp,
            [new PayRuleAssignment(new PayRule { ShiftDateStrategy = strategy }, new LocalDate(2000, 1, 1))],
            []);

    [Fact]
    public void SplitAtMidnight_OvernightShift_PaysAcrossTwoDays_EndToEnd()
    {
        // 22:00 Jan 2 → 06:00 Jan 3 at $20: 2 hrs land on Jan 2, 6 hrs on Jan 3. Gross is unchanged
        // (8 × $20 = $160) — the split only changes which day the hours are attributed to.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var punches = new List<Punch> { In(emp, At(2, 22)), Out(emp, At(3, 6)) };

        var result = PayCalculator.Calculate(punches, Ctx(emp, ShiftDateStrategy.SplitAtMidnight));

        Assert.Equal(160m, result.GrossPay);

        // Two distinct shifts, one per calendar day, rather than a single shift on one date.
        var shiftDates = result.Workweeks
            .SelectMany(w => w.LineItems)
            .Select(l => l.ShiftDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        Assert.Equal([new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 3)], shiftDates);
    }

    [Fact]
    public void FirstPunchStrategy_SameOvernightShift_StaysOnOneDay_EndToEnd()
    {
        // Contrast: the default strategy keeps the whole 8 hrs on the start date.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var punches = new List<Punch> { In(emp, At(2, 22)), Out(emp, At(3, 6)) };

        var result = PayCalculator.Calculate(punches, Ctx(emp, ShiftDateStrategy.FirstPunchLocalDate));

        Assert.Equal(160m, result.GrossPay);

        var shiftDates = result.Workweeks
            .SelectMany(w => w.LineItems)
            .Select(l => l.ShiftDate)
            .Distinct()
            .ToList();

        Assert.Equal([new LocalDate(2023, 1, 2)], shiftDates);
    }

    [Fact]
    public void SplitAtMidnight_FeedsDailyOvertimePerDay_EndToEnd()
    {
        // California daily OT at 8 hrs. A 20:00 → 10:00 shift (14 hrs) split at midnight becomes
        // 4 hrs on Jan 2 and 10 hrs on Jan 3, so only the Jan 3 piece breaches the daily threshold.
        // Unsplit, all 14 hours would sit on Jan 2 and 6 of them would be daily OT.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var payRule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.SplitAtMidnight };
        payRule.OvertimeRule.HasDailyOvertime = true;
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(payRule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch> { In(emp, At(2, 20)), Out(emp, At(3, 10)) };

        var result = PayCalculator.Calculate(punches, ctx);

        // 14 hrs straight = $280; daily OT premium on the 2 hrs past 8 on Jan 3 = 2 × 0.5 × $20 = $20.
        Assert.Equal(300m, result.GrossPay);
    }
}
