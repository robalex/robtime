using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class RangeDifferentialQualifierTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    // A dated shift carrying already-computed differentials. The qualifier reads only ShiftDate and
    // Differentials, so punch pairs are irrelevant here.
    private static Shift ShiftOn(LocalDate date, params AppliedDifferential[] diffs)
        => new() { ShiftDate = date, Differentials = diffs };

    private static AppliedDifferential Diff(string code, decimal hours, decimal amount = 0)
        => new() { Code = code, Hours = hours, Amount = amount };

    private PipelineContext Ctx(params DifferentialRule[] rules)
        => new(_emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], rules);

    // A Monday..Friday range differential; Jan 2 2023 is a Monday, so its week's active days all
    // anchor to the same occurrence.
    private static DifferentialRule MonToFri(string code, decimal minHours) => new()
    {
        Code = code,
        DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
        DayOfWeekRangeStart = IsoDayOfWeek.Monday,
        DayOfWeekRangeEnd = IsoDayOfWeek.Friday,
        MinHoursInRange = minHours,
    };

    private static IReadOnlyList<AppliedDifferential> DiffsOf(IEnumerable<Shift> shifts)
        => shifts.SelectMany(s => s.Differentials).ToList();

    [Fact]
    public void BelowRangeThreshold_StripsAcrossTheOccurrence()
    {
        // 3 + 3 = 6 qualifying hours in the Jan 2 week, threshold 20 → stripped everywhere
        var rule = MonToFri("LOYALTY", 20m);
        var shifts = new[]
        {
            ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 3m)),
            ShiftOn(new LocalDate(2023, 1, 3), Diff("LOYALTY", 3m)),
        };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Empty(DiffsOf(result));
    }

    [Fact]
    public void MeetsRangeThreshold_SummedAcrossTheOccurrence_Keeps()
    {
        // 10 + 12 = 22 ≥ 20 in one occurrence → kept
        var rule = MonToFri("LOYALTY", 20m);
        var shifts = new[]
        {
            ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 10m)),
            ShiftOn(new LocalDate(2023, 1, 4), Diff("LOYALTY", 12m)),
        };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Equal(2, DiffsOf(result).Count);
    }

    [Fact]
    public void ExactlyAtThreshold_Qualifies()
    {
        var rule = MonToFri("LOYALTY", 20m);
        var shifts = new[]
        {
            ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 8m)),
            ShiftOn(new LocalDate(2023, 1, 3), Diff("LOYALTY", 12m)),
        };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Equal(2, DiffsOf(result).Count);
    }

    [Fact]
    public void EachOccurrenceJudgedIndependently_IndependentOfPayrollWeek()
    {
        // Two Mon..Fri occurrences: the Jan 2 week clears 20 (10+12=22, kept); the Jan 9 week falls
        // short (6, stripped). Occurrences reset — nothing to do with FLSA week grouping.
        var rule = MonToFri("LOYALTY", 20m);
        var shifts = new[]
        {
            ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 10m)),   // week of Jan 2
            ShiftOn(new LocalDate(2023, 1, 4), Diff("LOYALTY", 12m)),   // week of Jan 2
            ShiftOn(new LocalDate(2023, 1, 9), Diff("LOYALTY", 6m)),    // week of Jan 9
        };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Equal(2, result[0].Differentials.Count + result[1].Differentials.Count);
        Assert.Empty(result[2].Differentials);
    }

    [Fact]
    public void NonRangeRule_WithThreshold_IsUntouched()
    {
        // A DaysOfWeek rule can't carry a range threshold — this stage only touches
        // ConsecutiveDayRange rules, so its tiny total is left alone.
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday },
            MinHoursInRange = 20m,   // ignored: not a ConsecutiveDayRange rule
        };
        var shifts = new[] { ShiftOn(new LocalDate(2023, 1, 2), Diff("NIGHT", 1m)) };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Single(DiffsOf(result));
    }

    [Fact]
    public void RangeRule_WithoutThreshold_IsUntouched()
    {
        var rule = MonToFri("LOYALTY", 0m);   // no threshold
        var shifts = new[] { ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 1m)) };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(rule));

        Assert.Single(DiffsOf(result));
    }

    [Fact]
    public void OnlyFailingCodeStripped_OtherCodesOnSameShiftKept()
    {
        var loyalty = MonToFri("LOYALTY", 20m);
        var night = new DifferentialRule { Code = "NIGHT" };   // EveryDay, no threshold
        var shifts = new[]
        {
            ShiftOn(new LocalDate(2023, 1, 2), Diff("LOYALTY", 6m), Diff("NIGHT", 6m)),
        };

        var result = RangeDifferentialQualifier.Execute(shifts, Ctx(loyalty, night));

        var remaining = DiffsOf(result);
        Assert.Single(remaining);
        Assert.Equal("NIGHT", remaining[0].Code);
    }
}
