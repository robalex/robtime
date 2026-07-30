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
        => Segments(rule, pair, ctx).Sum(s => (decimal)(s.End - s.Start).TotalHours);

    /// <summary>Same reasoning as PerDayQualifyingHoursCalculator.Segments: the actual qualifying
    /// intervals (up to two, one per occurrence a pair can reach) rather than just their summed
    /// hours. Calculate() above sums these.</summary>
    public static IReadOnlyList<QualifyingSegment> Segments(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        var inInstant = pair.InPunch!.EffectiveTime;
        var outInstant = pair.OutPunch!.EffectiveTime;

        var baseAnchor = DayOfWeekRange.OccurrenceAnchor(inInstant.InZone(zone).Date, rule.DayOfWeekRangeStart);
        int rangeLengthDays = DayOfWeekRange.Length(rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd);

        var segments = new List<QualifyingSegment>();
        foreach (var anchor in new[] { baseAnchor, baseAnchor.PlusDays(7) })
        {
            var span = OccurrenceSpan(rule, anchor, rangeLengthDays, zone);
            var overlapStart = inInstant > span.Start ? inInstant : span.Start;
            var overlapEnd = outInstant < span.End ? outInstant : span.End;
            if (overlapEnd > overlapStart)
            {
                segments.Add(new QualifyingSegment { Start = overlapStart, End = overlapEnd });
            }
        }

        return segments;
    }

    // The [start, end) span of the range occurrence anchored on `anchor` (a range-start weekday).
    // All-day (WindowStart == WindowEnd) covers whole days: midnight of the first day to midnight
    // after the last. Otherwise WindowStart on the first day to WindowEnd on the last. end is always
    // after start: PipelineContext rejects a single-day range, so start and end sit on different
    // days and the day gap dominates any within-day window difference.
    //
    // internal (not private): DifferentialZoneProjector reuses this exact span arithmetic to project
    // where a ConsecutiveDayRange rule *could* apply, independent of any punches — same reasoning as
    // DifferentialDaySchedule.AppliesOn being shared rather than reimplemented.
    internal static Interval OccurrenceSpan(
        DifferentialRule rule, LocalDate anchor, int rangeLengthDays, DateTimeZone zone)
    {
        if (rule.IsAllDay)
        {
            return new Interval(
                anchor.AtMidnight().InZoneLeniently(zone).ToInstant(),
                anchor.PlusDays(rangeLengthDays).AtMidnight().InZoneLeniently(zone).ToInstant());
        }

        return new Interval(
            anchor.At(rule.WindowStart).InZoneLeniently(zone).ToInstant(),
            anchor.PlusDays(rangeLengthDays - 1).At(rule.WindowEnd).InZoneLeniently(zone).ToInstant());
    }
}
