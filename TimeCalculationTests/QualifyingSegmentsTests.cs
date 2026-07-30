using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Segments() must always agree with Calculate() (same overlap math, just reported as real Instants
/// instead of summed hours) — the differential sandbox's explainer depends on that agreement to show
/// exactly *when* a qualifying interval happened, not just how many hours it added up to.
/// </summary>
public class QualifyingSegmentsTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private PunchPair PairUtc(int startHour, int endHour, int day = 2)
    {
        var midnight = Instant.FromUtc(2023, 1, day, 0, 0);
        var inP = TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(startHour), PunchKind.In, _emp);
        var outP = TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(endHour), PunchKind.Out, _emp);
        return new PunchPair { InPunch = inP, OutPunch = outP };
    }

    private PipelineContext Ctx(HolidayCalendar? holidays = null) => new(_emp, [], [], [], holidays);

    [Fact]
    public void PerDay_NonWrappingWindow_ReturnsOneSegmentWithExactInstants()
    {
        // Monday Jan 2, 2023. Window 12:00-16:00, worked 09:00-17:00 — overlap is exactly the window.
        var rule = new DifferentialRule
        {
            Code = "MID",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday },
            WindowStart = new LocalTime(12, 0),
            WindowEnd = new LocalTime(16, 0),
        };
        var pair = PairUtc(9, 17);

        var segments = PerDayQualifyingHoursCalculator.Segments(rule, pair, Ctx());

        var segment = Assert.Single(segments);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 12, 0), segment.Start);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 16, 0), segment.End);
        Assert.Equal(4m, PerDayQualifyingHoursCalculator.Calculate(rule, pair, Ctx()));
    }

    [Fact]
    public void PerDay_MidnightWrappingWindow_ReturnsTwoSegments_SummingToCalculate()
    {
        // Window 22:00-06:00, worked across midnight 20:00 (Jan 2) - 08:00 (Jan 3).
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.EveryDay,
            WindowStart = new LocalTime(22, 0),
            WindowEnd = new LocalTime(6, 0),
        };
        var pair = PairUtc(20, 32); // 20:00 Jan 2 -> 08:00 Jan 3 (32 = 24 + 8)

        var segments = PerDayQualifyingHoursCalculator.Segments(rule, pair, Ctx());

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.Start == Instant.FromUtc(2023, 1, 2, 22, 0) && s.End == Instant.FromUtc(2023, 1, 3, 0, 0));
        Assert.Contains(segments, s => s.Start == Instant.FromUtc(2023, 1, 3, 0, 0) && s.End == Instant.FromUtc(2023, 1, 3, 6, 0));

        var totalHours = segments.Sum(s => (decimal)(s.End - s.Start).TotalHours);
        Assert.Equal(PerDayQualifyingHoursCalculator.Calculate(rule, pair, Ctx()), totalHours);
    }

    [Fact]
    public void PerDay_AllDay_ReturnsOneSegmentSpanningTheWholeWorkedInterval()
    {
        var rule = new DifferentialRule { Code = "ALLDAY", DayScheduleMode = DayScheduleMode.EveryDay };
        var pair = PairUtc(9, 17);

        var segments = PerDayQualifyingHoursCalculator.Segments(rule, pair, Ctx());

        var segment = Assert.Single(segments);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 9, 0), segment.Start);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 17, 0), segment.End);
    }

    [Fact]
    public void PerDay_RuleNotActiveOnWorkedDay_ReturnsNoSegments()
    {
        var rule = new DifferentialRule
        {
            Code = "TUESONLY",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Tuesday },
        };
        var pair = PairUtc(9, 17); // Jan 2, 2023 is a Monday

        Assert.Empty(PerDayQualifyingHoursCalculator.Segments(rule, pair, Ctx()));
    }

    [Fact]
    public void ContinuousRange_ReturnsSegmentMatchingCalculate()
    {
        var rule = new DifferentialRule
        {
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Friday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Sunday,
        };
        // Friday Jan 6, 2023 09:00 - 17:00, fully inside the Fri-Sun occurrence.
        var pair = PairUtc(9, 17, day: 6);

        var segments = ContinuousRangeQualifyingHoursCalculator.Segments(rule, pair, Ctx());

        var segment = Assert.Single(segments);
        Assert.Equal(Instant.FromUtc(2023, 1, 6, 9, 0), segment.Start);
        Assert.Equal(Instant.FromUtc(2023, 1, 6, 17, 0), segment.End);

        var totalHours = segments.Sum(s => (decimal)(s.End - s.Start).TotalHours);
        Assert.Equal(ContinuousRangeQualifyingHoursCalculator.Calculate(rule, pair, Ctx()), totalHours);
    }
}
