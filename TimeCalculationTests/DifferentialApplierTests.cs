using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;
using Xunit;

namespace TimeCalculationTests;

public class DifferentialApplierTests
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
        var result = DifferentialApplier.ApplyDifferentials([shift], ctx);
        Assert.Empty(result[0].Differentials);
    }

    [Fact]
    public void ConsecutiveDayRange_WithSameStartAndEndDay_IsRejected()
    {
        // A single-day "range" is a DaysOfWeek selection; the context rejects it so the continuous
        // span can never invert (single day + wrapping window).
        var rule = new DifferentialRule
        {
            Code = "BAD",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Monday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Monday,
        };

        Assert.Throws<ArgumentException>(() => Ctx(_emp, rule));
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
        var result = DifferentialApplier.ApplyDifferentials([shift], Ctx(_emp, rule));

        Assert.Single(result[0].Differentials);
        var diff = result[0].Differentials[0];
        Assert.Equal("NIGHT", diff.Code);
        Assert.Equal(4m, diff.Hours);
        Assert.Equal(8m, diff.Amount);   // 4 hrs × $2
        Assert.Equal(2m, diff.AdjustmentValue);   // the configured $/hr, carried onto the result
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
        var result = DifferentialApplier.ApplyDifferentials([shift], Ctx(_emp, rule));

        Assert.Equal(8m, result[0].Differentials[0].Hours);
        Assert.Equal(16m, result[0].Differentials[0].Amount);
        Assert.Equal(0.10m, result[0].Differentials[0].AdjustmentValue);
    }

    [Fact]
    public void WeekendFilter_OnlyAppliesOnMatchingDays()
    {
        // Rule active only on Saturday; Monday shift should not qualify
        var rule = new DifferentialRule
        {
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday },
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3m,
        };
        var monday = ShiftUtc(9, 17, day: 2);        // Jan 2 2023 = Monday
        var saturday = ShiftUtc(9, 17, day: 7);      // Jan 7 2023 = Saturday

        var mon = DifferentialApplier.ApplyDifferentials([monday], Ctx(_emp, rule));
        var sat = DifferentialApplier.ApplyDifferentials([saturday], Ctx(_emp, rule));

        Assert.Empty(mon[0].Differentials);
        Assert.Single(sat[0].Differentials);
        Assert.Equal(24m, sat[0].Differentials[0].Amount);   // 8 hrs × $3
    }

    [Fact]
    public void HolidaysMode_RequiresHolidayCalendarMatch()
    {
        var rule = new DifferentialRule
        {
            Code = "HOLIDAY",
            DayScheduleMode = DayScheduleMode.Holidays,
            AdjustmentType = DifferentialAdjustmentType.FixedBonus,
            AdjustmentValue = 50m,
        };
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 2)]);

        var onHoliday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 2)], Ctx(_emp, rule, holidays));
        var offHoliday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 3)], Ctx(_emp, rule, holidays));

        Assert.Single(onHoliday[0].Differentials);
        Assert.Equal(50m, onHoliday[0].Differentials[0].Amount);   // fixed bonus once
        Assert.Empty(offHoliday[0].Differentials);
    }

    [Fact]
    public void DayOfWeekRange_SameWeek_OnlyAppliesWithinRange()
    {
        // Tuesday..Thursday (Jan 3–5, 2023): Wednesday qualifies, Friday does not
        var rule = new DifferentialRule
        {
            Code = "MIDWEEK",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Tuesday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Thursday,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3m,
        };
        var wednesday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 4)], Ctx(_emp, rule));
        var friday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 6)], Ctx(_emp, rule));

        Assert.Single(wednesday[0].Differentials);
        Assert.Empty(friday[0].Differentials);
    }

    [Fact]
    public void DayOfWeekRange_WrapsPastSunday()
    {
        // Thursday..Tuesday (a long-weekend differential): Thursday (Jan 5) qualifies,
        // Wednesday (Jan 4, the one day excluded) does not.
        var rule = new DifferentialRule
        {
            Code = "LONG_WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Thursday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Tuesday,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3m,
        };
        var thursday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 5)], Ctx(_emp, rule));
        var wednesday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 4)], Ctx(_emp, rule));

        Assert.Single(thursday[0].Differentials);
        Assert.Empty(wednesday[0].Differentials);
    }

    [Fact]
    public void ConsecutiveDayRange_Window_IsOneContinuousSpan_NotPerDay()
    {
        // Range Thu→Mon with a noon–5pm window is a single span: noon Thursday to 5pm Monday, with
        // the interior days fully covered — NOT noon–5pm on each day.
        var rule = new DifferentialRule
        {
            Code = "LONG_WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Thursday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Monday,
            WindowStart = new LocalTime(12, 0),
            WindowEnd = new LocalTime(17, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
        };

        // Thursday Jan 5 2023, 14:00–20:00: the span runs continuously past 5pm, so the whole
        // 6 hours qualify (a per-day window would cap it at 17:00 → 3h).
        var thu = DifferentialApplier.ApplyDifferentials([ShiftUtc(14, 20, day: 5)], Ctx(_emp, rule));
        Assert.Equal(6m, thu[0].Differentials[0].Hours);
        Assert.Equal(12m, thu[0].Differentials[0].Amount);

        // A full interior day (Friday Jan 6, 09:00–17:00) is entirely inside the span → 8h.
        var fri = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 6)], Ctx(_emp, rule));
        Assert.Equal(8m, fri[0].Differentials[0].Hours);

        // Monday Jan 9 is the last day: 15:00–19:00 qualifies only up to the 17:00 end → 2h.
        var mon = DifferentialApplier.ApplyDifferentials([ShiftUtc(15, 19, day: 9)], Ctx(_emp, rule));
        Assert.Equal(2m, mon[0].Differentials[0].Hours);
    }

    [Fact]
    public void DaysOfWeek_Window_AppliesPerDay()
    {
        // Contrast with the range: Mon & Thu with a noon–5pm window applies that window on each day
        // independently, so the same Thursday 14:00–20:00 shift is capped at 17:00 → 3h.
        var rule = new DifferentialRule
        {
            Code = "SPLIT",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday, IsoDayOfWeek.Thursday },
            WindowStart = new LocalTime(12, 0),
            WindowEnd = new LocalTime(17, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
        };

        var thu = DifferentialApplier.ApplyDifferentials([ShiftUtc(14, 20, day: 5)], Ctx(_emp, rule));
        Assert.Equal(3m, thu[0].Differentials[0].Hours);

        // Wednesday isn't listed → nothing qualifies.
        var wed = DifferentialApplier.ApplyDifferentials([ShiftUtc(14, 20, day: 4)], Ctx(_emp, rule));
        Assert.Empty(wed[0].Differentials);
    }

    [Fact]
    public void SpecificDates_RequiresDateMatch()
    {
        // Corporate holiday (e.g. a company anniversary) not in any federal HolidayCalendar
        var rule = new DifferentialRule
        {
            Code = "CORP_HOLIDAY",
            DayScheduleMode = DayScheduleMode.SpecificDates,
            SpecificDates = new HashSet<LocalDate> { new(2023, 1, 2) },
            AdjustmentType = DifferentialAdjustmentType.FixedBonus,
            AdjustmentValue = 25m,
        };

        var onDate = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 2)], Ctx(_emp, rule));
        var offDate = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 3)], Ctx(_emp, rule));

        Assert.Single(onDate[0].Differentials);
        Assert.Equal(25m, onDate[0].Differentials[0].Amount);
        Assert.Empty(offDate[0].Differentials);
    }

    [Fact]
    public void DayScheduleMode_IsExclusive_OtherModesFieldsIgnored()
    {
        // Mode is DaysOfWeek (Tuesday only). SpecificDates and a range are also populated but must
        // be ignored entirely — only the selected mode's field is consulted.
        var rule = new DifferentialRule
        {
            Code = "MIXED",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Tuesday },
            SpecificDates = new HashSet<LocalDate> { new(2023, 1, 2) },  // Monday — ignored
            DayOfWeekRangeStart = IsoDayOfWeek.Friday,                   // ignored
            DayOfWeekRangeEnd = IsoDayOfWeek.Sunday,                     // ignored
            AdjustmentType = DifferentialAdjustmentType.FixedBonus,
            AdjustmentValue = 25m,
        };

        // Jan 2 = Monday: in SpecificDates and would need consulting other modes, but mode is
        // DaysOfWeek(Tue) → does not qualify.
        var monday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 2)], Ctx(_emp, rule));
        // Jan 3 = Tuesday: matches the active mode.
        var tuesday = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17, day: 3)], Ctx(_emp, rule));

        Assert.Empty(monday[0].Differentials);
        Assert.Single(tuesday[0].Differentials);
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
        var result = DifferentialApplier.ApplyDifferentials([shift], Ctx(_emp, rule));

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
        var result = DifferentialApplier.ApplyDifferentials([shift], Ctx(_emp, rule));

        Assert.Single(result[0].Differentials);
        Assert.Equal(4m, result[0].Differentials[0].Hours);
    }

    // ── Stacking ──

    private static PipelineContext CtxRules(Employee emp, IReadOnlyList<DifferentialRule> rules, HolidayCalendar? holidays = null)
        => new(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], rules, holidays);

    [Fact]
    public void OvernightAndHoliday_Stack_ByDefault()
    {
        // Shift 22:00 (a holiday) → 02:00 next day. Holiday diff pays for hours on the holiday
        // date (22:00–24:00 = 2h); overnight diff pays for the whole 22:00–06:00 window (4h).
        // With no exclusivity group they stack — overlapping 22:00–24:00 hours earn both.
        var holiday = new DifferentialRule
        {
            Code = "HOLIDAY", DayScheduleMode = DayScheduleMode.Holidays,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m,
        };
        var overnight = new DifferentialRule
        {
            Code = "NIGHT", WindowStart = new LocalTime(22, 0), WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 3m,
        };
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 2)]);
        var ctx = CtxRules(_emp, [holiday, overnight], holidays);

        var diffs = DifferentialApplier.ApplyDifferentials([ShiftUtc(22, 26, day: 2)], ctx)[0].Differentials;

        Assert.Equal(2, diffs.Count);
        Assert.Equal(10m, diffs.Single(d => d.Code == "HOLIDAY").Amount);   // 2h × $5
        Assert.Equal(12m, diffs.Single(d => d.Code == "NIGHT").Amount);     // 4h × $3
    }

    [Fact]
    public void SameExclusivityGroup_OnlyHighestApplies()
    {
        // Two all-day flat differentials in one group on an 8-hr shift → only the higher ($5) applies
        var low  = new DifferentialRule { Code = "LOW",  ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m };
        var high = new DifferentialRule { Code = "HIGH", ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m };
        var ctx = CtxRules(_emp, [low, high]);

        var diffs = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17)], ctx)[0].Differentials;

        Assert.Single(diffs);
        Assert.Equal("HIGH", diffs[0].Code);
        Assert.Equal(40m, diffs[0].Amount);   // 8h × $5
    }

    [Fact]
    public void DifferentGroups_StillStack()
    {
        // Same two rules, but different (or absent) groups → both apply
        var a = new DifferentialRule { Code = "A", ExclusivityGroup = "g1",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m };
        var b = new DifferentialRule { Code = "B", ExclusivityGroup = "g2",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m };
        var ctx = CtxRules(_emp, [a, b]);

        var diffs = DifferentialApplier.ApplyDifferentials([ShiftUtc(9, 17)], ctx)[0].Differentials;

        Assert.Equal(2, diffs.Count);
    }
}
