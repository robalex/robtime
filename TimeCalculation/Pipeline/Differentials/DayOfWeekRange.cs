using NodaTime;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// Pure primitives for a consecutive day-of-week range (e.g. Thursday..Tuesday) that may wrap past
/// Sunday. Shared by the differential calculators and RangeDifferentialQualifier so they all agree
/// on range membership, length, and which occurrence a date falls in.
/// </summary>
internal static class DayOfWeekRange
{
    // Inclusive membership test. May wrap past Sunday (start later in the week than end), e.g.
    // Thursday..Tuesday includes Thu, Fri, Sat, Sun, Mon, Tue.
    internal static bool Contains(IsoDayOfWeek day, IsoDayOfWeek start, IsoDayOfWeek end)
        => start <= end
            ? day >= start && day <= end
            : day >= start || day <= end;

    // Number of days in the inclusive range, wrapping past Sunday (Thursday..Tuesday = 6).
    internal static int Length(IsoDayOfWeek start, IsoDayOfWeek end)
        => (((int)end - (int)start + 7) % 7) + 1;

    // Most recent date on or before `date` whose weekday is `rangeStart` — the start of the range
    // occurrence this date belongs to. Anchored on the range's own start weekday (cf.
    // WorkweekGrouper, which anchors on PayRule.WorkweekStartDay).
    internal static LocalDate OccurrenceAnchor(LocalDate date, IsoDayOfWeek rangeStart)
    {
        int diff = ((int)date.DayOfWeek - (int)rangeStart + 7) % 7;
        return date.PlusDays(-diff);
    }
}
