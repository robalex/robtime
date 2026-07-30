using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

public static class PerDayQualifyingHoursCalculator
{
    public static decimal Calculate(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
        => Segments(rule, pair, ctx).Sum(s => (decimal)(s.End - s.Start).TotalHours);

    /// <summary>
    /// The actual qualifying intervals — where the rule was both active (day schedule) and inside
    /// its time-of-day window, intersected with real worked time. Distinct from
    /// DifferentialZoneProjector's zones, which show where a rule *could* apply with no punches
    /// involved; these show what actually happened. Calculate() above is just the sum of these
    /// segments' hours — one implementation of the overlap math, two consumers (the applier's
    /// totals, and the sandbox explainer's detail).
    /// </summary>
    public static IReadOnlyList<QualifyingSegment> Segments(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        var segments = new List<QualifyingSegment>();
        foreach (var daySegment in SplitWorkedIntervalIntoDaySegments(pair, ctx.EmployeeTimeZone))
        {
            if (!DifferentialDaySchedule.AppliesOn(rule, daySegment.Date, ctx.HolidayCalendar))
            {
                continue;
            }

            foreach (var range in WindowOverlapRanges(daySegment.StartSec, daySegment.EndSec, rule))
            {
                if (range.End <= range.Start)
                {
                    continue;
                }
                segments.Add(new QualifyingSegment
                {
                    Start = daySegment.DayStart + Duration.FromSeconds(range.Start),
                    End = daySegment.DayStart + Duration.FromSeconds(range.End),
                });
            }
        }
        return segments;
    }

    private static IEnumerable<DaySegment> SplitWorkedIntervalIntoDaySegments(PunchPair workedPair, DateTimeZone timeZone)
    {
        var current = workedPair.InPunch!.EffectiveTime;
        var end = workedPair.OutPunch!.EffectiveTime;
        while (current < end)
        {
            var zdt = current.InZone(timeZone);
            var date = zdt.Date;
            var dayStart = date.AtMidnight().InZoneLeniently(timeZone).ToInstant();
            var nextMidnight = date.PlusDays(1).AtMidnight().InZoneLeniently(timeZone).ToInstant();
            var segEnd = end < nextMidnight ? end : nextMidnight;

            int startSec = SecondsOf(zdt.TimeOfDay);
            int endSec = segEnd == nextMidnight ? NodaConstants.SecondsPerDay : SecondsOf(segEnd.InZone(timeZone).TimeOfDay);

            yield return new DaySegment(date, dayStart, startSec, endSec);
            current = nextMidnight;
        }
    }

    // Same overlap logic this class always had, restructured to hand back the actual overlapping
    // second-ranges (up to two, for a midnight-wrapping window) instead of just their summed
    // duration — Calculate() sums their lengths, Segments() converts them to real Instants.
    private static IEnumerable<SecondsRange> WindowOverlapRanges(int workTimeStart, int workTimeEnd, DifferentialRule rule)
    {
        if (rule.IsAllDay)
        {
            yield return new SecondsRange(workTimeStart, workTimeEnd);
            yield break;
        }

        int windowStartSeconds = SecondsOf(rule.WindowStart);
        int windowEndSeconds = SecondsOf(rule.WindowEnd);
        var windowSpansMidnight = windowStartSeconds >= windowEndSeconds;

        if (windowSpansMidnight)
        {
            yield return Overlap(workTimeStart, workTimeEnd, windowStartSeconds, NodaConstants.SecondsPerDay);
            yield return Overlap(workTimeStart, workTimeEnd, 0, windowEndSeconds);
        }
        else
        {
            yield return Overlap(workTimeStart, workTimeEnd, windowStartSeconds, windowEndSeconds);
        }
    }

    private static SecondsRange Overlap(int aStart, int aEnd, int bStart, int bEnd)
        => new(Math.Max(aStart, bStart), Math.Min(aEnd, bEnd));

    private static int SecondsOf(LocalTime t) => t.Hour * 3600 + t.Minute * 60 + t.Second;

    private readonly record struct SecondsRange(int Start, int End);
}
