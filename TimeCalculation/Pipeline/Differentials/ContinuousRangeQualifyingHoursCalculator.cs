using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

public static class ContinuousRangeQualifyingHoursCalculator
{
    // The rule's window bounds one continuous span per range occurrence: WindowStart on the range's
    // first day (its start weekday) through WindowEnd on its last day. Interior days are fully
    // covered. A pair (at most one shift long) can reach the occurrence its start date anchors to,
    // or — when that date sits in the gap before the range begins — the next one, so both are summed.
    public static decimal Calculate(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        var inInstant = pair.InPunch!.EffectiveTime;
        var outInstant = pair.OutPunch!.EffectiveTime;

        var baseAnchor = DayOfWeekRange.OccurrenceAnchor(inInstant.InZone(zone).Date, rule.DayOfWeekRangeStart);
        int rangeLenDays = DayOfWeekRange.Length(rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd);

        decimal hours = 0;
        foreach (var anchor in new[] { baseAnchor, baseAnchor.PlusDays(7) })
        {
            var (spanStart, spanEnd) = OccurrenceSpan(rule, anchor, rangeLenDays, zone);
            var overlapStart = inInstant > spanStart ? inInstant : spanStart;
            var overlapEnd = outInstant < spanEnd ? outInstant : spanEnd;
            if (overlapEnd > overlapStart)
            {
                hours += (decimal)(overlapEnd - overlapStart).TotalHours;
            }
        }

        return hours;
    }

    // The [start, end) instants of the range occurrence anchored on `anchor` (a range-start weekday).
    // All-day (WindowStart == WindowEnd) covers whole days: midnight of the first day to midnight
    // after the last. Otherwise WindowStart on the first day to WindowEnd on the last.
    private static (Instant Start, Instant End) OccurrenceSpan(
        DifferentialRule rule, LocalDate anchor, int rangeLenDays, DateTimeZone zone)
    {
        if (rule.IsAllDay)
        {
            return (
                anchor.AtMidnight().InZoneLeniently(zone).ToInstant(),
                anchor.PlusDays(rangeLenDays).AtMidnight().InZoneLeniently(zone).ToInstant());
        }

        return (
            anchor.At(rule.WindowStart).InZoneLeniently(zone).ToInstant(),
            anchor.PlusDays(rangeLenDays - 1).At(rule.WindowEnd).InZoneLeniently(zone).ToInstant());
    }
}
