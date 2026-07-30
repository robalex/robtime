using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;
using Xunit;

namespace TimeCalculationTests;

public class DifferentialZoneProjectorTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    // DifferentialZoneProjector only reads ctx.EmployeeTimeZone and ctx.HolidayCalendar — no
    // PayRuleAssignment/ActiveDifferentialCodes gating, since a zone is "where could this rule ever
    // apply," independent of whether any PayRule currently opts into it.
    private PipelineContext Ctx(HolidayCalendar? holidays = null)
        => new(_emp, [], [], [], holidays);

    [Fact]
    public void DaysOfWeek_NonWrappingWindow_ProjectsOneZonePerActiveDay()
    {
        var rule = new DifferentialRule
        {
            Code = "MON",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday },
            WindowStart = new LocalTime(9, 0),
            WindowEnd = new LocalTime(17, 0),
        };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 8), Ctx());

        var zone = Assert.Single(zones);
        Assert.Equal("MON", zone.Code);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 9, 0), zone.Start);
        Assert.Equal(Instant.FromUtc(2023, 1, 2, 17, 0), zone.End);
    }

    [Fact]
    public void DaysOfWeek_MidnightWrappingWindow_ProjectsMorningAndEveningPieces()
    {
        // 22:00-06:00 on a Monday: the early-morning 00:00-06:00 piece is attributed to Monday (the
        // tail of the *previous* night's window) and the 22:00-24:00 piece continues into Tuesday —
        // matching PerDayQualifyingHoursCalculator's own per-day wrap handling.
        var rule = new DifferentialRule
        {
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday },
            WindowStart = new LocalTime(22, 0),
            WindowEnd = new LocalTime(6, 0),
        };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 8), Ctx());

        Assert.Equal(2, zones.Count);
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2023, 1, 2, 0, 0) && z.End == Instant.FromUtc(2023, 1, 2, 6, 0));
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2023, 1, 2, 22, 0) && z.End == Instant.FromUtc(2023, 1, 3, 0, 0));
    }

    [Fact]
    public void EveryDay_AllDay_ProjectsFullDayZonesForEachDayInWindow()
    {
        var rule = new DifferentialRule { Code = "ALLDAY", DayScheduleMode = DayScheduleMode.EveryDay };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 3), Ctx());

        Assert.Equal(2, zones.Count);
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2023, 1, 2, 0, 0) && z.End == Instant.FromUtc(2023, 1, 3, 0, 0));
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2023, 1, 3, 0, 0) && z.End == Instant.FromUtc(2023, 1, 4, 0, 0));
    }

    [Fact]
    public void Holidays_ProjectsOnlyOnCalendarHolidays()
    {
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 4)]);
        var rule = new DifferentialRule
        {
            Code = "HOLIDAY",
            DayScheduleMode = DayScheduleMode.Holidays,
            WindowStart = new LocalTime(8, 0),
            WindowEnd = new LocalTime(16, 0),
        };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 8), Ctx(holidays));

        var zone = Assert.Single(zones);
        Assert.Equal(Instant.FromUtc(2023, 1, 4, 8, 0), zone.Start);
        Assert.Equal(Instant.FromUtc(2023, 1, 4, 16, 0), zone.End);
    }

    [Fact]
    public void Holidays_WithNoHolidayCalendar_ProjectsNothing()
    {
        var rule = new DifferentialRule { Code = "HOLIDAY", DayScheduleMode = DayScheduleMode.Holidays };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 8), Ctx());

        Assert.Empty(zones);
    }

    [Fact]
    public void ConsecutiveDayRange_WrapsAcrossVisibleWindowEdges_ProjectsUnclippedOccurrences()
    {
        // Thursday..Tuesday (6-day range). A 7-day visible window [Jan 2, Jan 8] straddles two
        // occurrences: one that started the previous Thursday (Dec 29) and continues into the
        // window, and one that starts Thursday Jan 5 and continues past the window's end. Both are
        // returned in full, un-clipped — the frontend is responsible for marking "continues."
        var rule = new DifferentialRule
        {
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Thursday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Tuesday,
        };

        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 8), Ctx());

        Assert.Equal(2, zones.Count);
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2022, 12, 29, 0, 0) && z.End == Instant.FromUtc(2023, 1, 4, 0, 0));
        Assert.Contains(zones, z => z.Start == Instant.FromUtc(2023, 1, 5, 0, 0) && z.End == Instant.FromUtc(2023, 1, 11, 0, 0));
    }

    [Fact]
    public void ConsecutiveDayRange_OutsideWindow_ProjectsNothing()
    {
        var rule = new DifferentialRule
        {
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Thursday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Tuesday,
        };

        // A single Wednesday never overlaps a Thu..Tue occurrence.
        var zones = DifferentialZoneProjector.Project(rule, new LocalDate(2023, 1, 4), new LocalDate(2023, 1, 4), Ctx());

        Assert.Empty(zones);
    }
}
