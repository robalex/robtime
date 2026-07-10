using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 10 — Group into Workweeks.
/// Buckets WorkDays into FLSA 168-hour workweeks anchored on PayRule.WorkweekStartDay
/// (default Sunday) at midnight in the employee's timezone.  Each workweek's days are
/// numbered with their 1-based consecutive-day position (a calendar gap resets the streak),
/// which the 7th-consecutive-day overtime rule depends on.
///
/// The anchor day is read from the PayRule active on each day, so a normal (constant-anchor)
/// configuration groups exactly as expected.  If the anchor itself changes mid-week — a rare
/// judgment-call case — days are grouped by whichever anchor their own rule specifies.
/// </summary>
public static class Stage10_GroupIntoWeeks
{
    public static IReadOnlyList<Workweek> Execute(IReadOnlyList<WorkDay> days, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;

        return days
            .GroupBy(d => WeekStartFor(d.Date, AnchorFor(d, ctx)))
            .OrderBy(g => g.Key)
            .Select(g => new Workweek
            {
                StartDate = g.Key,
                StartInstant = g.Key.AtMidnight().InZoneLeniently(zone).ToInstant(),
                Days = AssignConsecutiveDayNumbers(g.OrderBy(d => d.Date).ToList()),
            })
            .ToList();
    }

    private static IsoDayOfWeek AnchorFor(WorkDay day, PipelineContext ctx)
    {
        var instant = day.Date.AtMidnight().InZoneLeniently(ctx.EmployeeTimeZone).ToInstant();
        return ctx.GetRuleAt(instant).WorkweekStartDay;
    }

    /// <summary>Most recent date on or before <paramref name="date"/> whose day-of-week is the anchor.</summary>
    private static LocalDate WeekStartFor(LocalDate date, IsoDayOfWeek anchor)
    {
        int diff = ((int)date.DayOfWeek - (int)anchor + 7) % 7;
        return date.PlusDays(-diff);
    }

    private static IReadOnlyList<WorkDay> AssignConsecutiveDayNumbers(List<WorkDay> orderedDays)
    {
        var result = new List<WorkDay>(orderedDays.Count);
        LocalDate? prev = null;
        int streak = 0;

        foreach (var day in orderedDays)
        {
            streak = prev is not null && day.Date == prev.Value.PlusDays(1) ? streak + 1 : 1;
            result.Add(day with { ConsecutiveDayNumber = streak });
            prev = day.Date;
        }

        return result;
    }
}
