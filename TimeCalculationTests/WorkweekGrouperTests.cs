using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class WorkweekGrouperTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private static WorkDay DayOn(LocalDate date, decimal hours = 8m)
    {
        var start = date.AtMidnight().InZoneLeniently(DateTimeZone.Utc).ToInstant() + Duration.FromHours(9);
        var end   = start + Duration.FromHours((double)hours);
        var inP  = new Punch { Kind = PunchKind.In,  PunchTime = start };
        var outP = new Punch { Kind = PunchKind.Out, PunchTime = end };
        var shift = new Shift { ShiftDate = date, PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP }] };
        return new WorkDay { Date = date, Shifts = [shift] };
    }

    // 2023: Jan 1 is a Sunday, Jan 2 a Monday.
    [Fact]
    public void DaysSpanningSundayAnchor_SplitIntoTwoWorkweeks()
    {
        // Default anchor = Sunday. Sat Jan 7 and Sun Jan 8 fall in different weeks.
        var days = new[] { DayOn(new LocalDate(2023, 1, 7)), DayOn(new LocalDate(2023, 1, 8)) };
        var weeks = WorkweekGrouper.Execute(days, TestEntityCreator.CreateContext(employee: _emp));

        Assert.Equal(2, weeks.Count);
        Assert.Equal(new LocalDate(2023, 1, 1), weeks[0].StartDate);   // week of Sun Jan 1
        Assert.Equal(new LocalDate(2023, 1, 8), weeks[1].StartDate);   // week of Sun Jan 8
    }

    [Fact]
    public void AllDaysWithinOneWeek_ProduceSingleWorkweek()
    {
        // Mon–Fri Jan 2–6 all belong to the Sun Jan 1 workweek
        var days = new[]
        {
            DayOn(new LocalDate(2023, 1, 2)), DayOn(new LocalDate(2023, 1, 3)),
            DayOn(new LocalDate(2023, 1, 4)), DayOn(new LocalDate(2023, 1, 5)),
            DayOn(new LocalDate(2023, 1, 6)),
        };
        var weeks = WorkweekGrouper.Execute(days, TestEntityCreator.CreateContext(employee: _emp));

        Assert.Single(weeks);
        Assert.Equal(new LocalDate(2023, 1, 1), weeks[0].StartDate);
        Assert.Equal(40m, weeks[0].TotalHours);
        Assert.Equal(Instant.FromUtc(2023, 1, 1, 0, 0), weeks[0].StartInstant);
    }

    [Fact]
    public void CustomAnchor_Monday_GroupsFromMonday()
    {
        var rule = new PayRule { WorkweekStartDay = IsoDayOfWeek.Monday };
        var ctx  = TestEntityCreator.CreateContext(rule, _emp);

        // Sun Jan 8 belongs to the Mon Jan 2 week; Mon Jan 9 starts a new week.
        var days = new[] { DayOn(new LocalDate(2023, 1, 8)), DayOn(new LocalDate(2023, 1, 9)) };
        var weeks = WorkweekGrouper.Execute(days, ctx);

        Assert.Equal(2, weeks.Count);
        Assert.Equal(new LocalDate(2023, 1, 2), weeks[0].StartDate);
        Assert.Equal(new LocalDate(2023, 1, 9), weeks[1].StartDate);
    }

    [Fact]
    public void ConsecutiveDayNumbers_IncrementAcrossAdjacentDays()
    {
        // Sun Jan 1 → Sat Jan 7, all in one workweek — days 1..7
        var days = Enumerable.Range(1, 7)
            .Select(d => DayOn(new LocalDate(2023, 1, d)))
            .ToArray();
        var weeks = WorkweekGrouper.Execute(days, TestEntityCreator.CreateContext(employee: _emp));

        Assert.Single(weeks);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, weeks[0].Days.Select(d => d.ConsecutiveDayNumber));
    }

    [Fact]
    public void ConsecutiveDayNumbers_ResetAfterCalendarGap()
    {
        // Work Sun, Mon, [skip Tue], Wed, Thu — streak resets on Wed
        var days = new[]
        {
            DayOn(new LocalDate(2023, 1, 1)), DayOn(new LocalDate(2023, 1, 2)),
            DayOn(new LocalDate(2023, 1, 4)), DayOn(new LocalDate(2023, 1, 5)),
        };
        var weeks = WorkweekGrouper.Execute(days, TestEntityCreator.CreateContext(employee: _emp));

        Assert.Single(weeks);
        Assert.Equal(new[] { 1, 2, 1, 2 }, weeks[0].Days.Select(d => d.ConsecutiveDayNumber));
    }

    [Fact]
    public void DaysProvidedOutOfOrder_AreSortedWithinWeek()
    {
        var days = new[]
        {
            DayOn(new LocalDate(2023, 1, 4)), DayOn(new LocalDate(2023, 1, 2)),
            DayOn(new LocalDate(2023, 1, 3)),
        };
        var weeks = WorkweekGrouper.Execute(days, TestEntityCreator.CreateContext(employee: _emp));

        Assert.Single(weeks);
        Assert.Equal(
            new[] { new LocalDate(2023, 1, 2), new LocalDate(2023, 1, 3), new LocalDate(2023, 1, 4) },
            weeks[0].Days.Select(d => d.Date));
        Assert.Equal(new[] { 1, 2, 3 }, weeks[0].Days.Select(d => d.ConsecutiveDayNumber));
    }
}
