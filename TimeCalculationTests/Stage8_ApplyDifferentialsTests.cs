using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class Stage8_ApplyDifferentialsTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    // Builds a single-pair shift from UTC wall-clock hours measured from midnight on 2023-01-day.
    // endHour may exceed 24 to express a shift crossing into the next day (e.g. 26 = 02:00 next day).
    private Shift ShiftUtc(int startHour, int endHour, decimal rate = 20m, int day = 2)
    {
        var midnight = Instant.FromUtc(2023, 1, day, 0, 0);
        var inP  = TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(startHour), PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(endHour),  PunchKind.Out, _emp);
        return new Shift
        {
            ShiftDate = new LocalDate(2023, 1, day),
            PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = rate }],
        };
    }

    private static PipelineContext Ctx(Employee emp, DifferentialRule rule, HolidayCalendar? holidays = null)
        => new(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [rule], holidays);

    [Fact]
    public void NoRules_ReturnsShiftsUnchanged()
    {
        var ctx = TestEntityCreator.CreateContext(employee: _emp);
        var shift = ShiftUtc(9, 17);
        var result = Stage8_ApplyDifferentials.Execute([shift], ctx);
        Assert.Empty(result[0].Differentials);
    }

    [Fact]
    public void NightWindow_FlatPerHour_AppliesToHoursInsideWindowOnly()
    {
        // Night window 22:00–06:00, work 20:00–02:00 → 4 qualifying hours (22:00–02:00)
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            WindowStart = new LocalTime(22, 0),
            WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
        };
        var shift = ShiftUtc(20, 26); // 20:00 → 02:00 next day
        var result = Stage8_ApplyDifferentials.Execute([shift], Ctx(_emp, rule));

        Assert.Single(result[0].Differentials);
        var diff = result[0].Differentials[0];
        Assert.Equal("NIGHT", diff.Code);
        Assert.Equal(4m, diff.Hours);
        Assert.Equal(8m, diff.Amount);   // 4 hrs × $2
    }

    [Fact]
    public void Multiplier_UsesPairRate()
    {
        // All-day 10% differential on an 8-hr shift at $20 → 8 × 20 × 0.10 = $16
        var rule = new DifferentialRule
        {
            Code = "SHIFT10",
            AdjustmentType = DifferentialAdjustmentType.Multiplier,
            AdjustmentValue = 0.10m,
        };
        var shift = ShiftUtc(9, 17, rate: 20m);
        var result = Stage8_ApplyDifferentials.Execute([shift], Ctx(_emp, rule));

        Assert.Equal(8m, result[0].Differentials[0].Hours);
        Assert.Equal(16m, result[0].Differentials[0].Amount);
    }

    [Fact]
    public void WeekendFilter_OnlyAppliesOnMatchingDays()
    {
        // Rule active only on Saturday; Monday shift should not qualify
        var rule = new DifferentialRule
        {
            Code = "WEEKEND",
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday },
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3m,
        };
        var monday = ShiftUtc(9, 17, day: 2);        // Jan 2 2023 = Monday
        var saturday = ShiftUtc(9, 17, day: 7);      // Jan 7 2023 = Saturday

        var mon = Stage8_ApplyDifferentials.Execute([monday], Ctx(_emp, rule));
        var sat = Stage8_ApplyDifferentials.Execute([saturday], Ctx(_emp, rule));

        Assert.Empty(mon[0].Differentials);
        Assert.Single(sat[0].Differentials);
        Assert.Equal(24m, sat[0].Differentials[0].Amount);   // 8 hrs × $3
    }

    [Fact]
    public void HolidaysOnly_RequiresHolidayCalendarMatch()
    {
        var rule = new DifferentialRule
        {
            Code = "HOLIDAY",
            HolidaysOnly = true,
            AdjustmentType = DifferentialAdjustmentType.FixedBonus,
            AdjustmentValue = 50m,
        };
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 2)]);

        var onHoliday = Stage8_ApplyDifferentials.Execute([ShiftUtc(9, 17, day: 2)], Ctx(_emp, rule, holidays));
        var offHoliday = Stage8_ApplyDifferentials.Execute([ShiftUtc(9, 17, day: 3)], Ctx(_emp, rule, holidays));

        Assert.Single(onHoliday[0].Differentials);
        Assert.Equal(50m, onHoliday[0].Differentials[0].Amount);   // fixed bonus once
        Assert.Empty(offHoliday[0].Differentials);
    }

    [Fact]
    public void MinHoursInWindow_NotMet_DoesNotApply()
    {
        // Requires 3 hrs in window; only 2 worked
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            WindowStart = new LocalTime(0, 0),
            WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
            MinHoursInWindow = 3m,
        };
        var shift = ShiftUtc(4, 8);   // 04:00–08:00 → only 2 hrs before 06:00
        var result = Stage8_ApplyDifferentials.Execute([shift], Ctx(_emp, rule));

        Assert.Empty(result[0].Differentials);
    }

    [Fact]
    public void MinHoursInWindow_Met_Applies()
    {
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            WindowStart = new LocalTime(0, 0),
            WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
            MinHoursInWindow = 3m,
        };
        var shift = ShiftUtc(2, 8);   // 02:00–08:00 → 4 hrs before 06:00
        var result = Stage8_ApplyDifferentials.Execute([shift], Ctx(_emp, rule));

        Assert.Single(result[0].Differentials);
        Assert.Equal(4m, result[0].Differentials[0].Hours);
    }
}
