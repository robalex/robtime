using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

public static class PerDayQualifyingHoursCalculator
{
    public static decimal Calculate(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        decimal hours = 0;
        foreach (var segment in SplitWorkedIntervalIntoDaySegments(pair, ctx.EmployeeTimeZone))
        {
            if (!RuleAppliesOn(rule, segment.Date, ctx))
            {
                continue;
            }

            int overlapSec = CalculateWindowOverlapSeconds(segment.StartSec, segment.EndSec, rule);

            if (overlapSec > 0)
            {
                hours += (decimal)overlapSec / 3600m;
            }
        }

        return hours;
    }

    private static IEnumerable<DaySegment> SplitWorkedIntervalIntoDaySegments(PunchPair workedPair, DateTimeZone timeZone)
    {
        var current = workedPair.InPunch!.EffectiveTime;
        var end = workedPair.OutPunch!.EffectiveTime;
        while (current < end)
        {
            var zdt = current.InZone(timeZone);
            var date = zdt.Date;
            var nextMidnight = date.PlusDays(1).AtMidnight().InZoneLeniently(timeZone).ToInstant();
            var segEnd = end < nextMidnight ? end : nextMidnight;

            int startSec = SecondsOf(zdt.TimeOfDay);
            int endSec = segEnd == nextMidnight ? NodaConstants.SecondsPerDay : SecondsOf(segEnd.InZone(timeZone).TimeOfDay);

            yield return new DaySegment(date, startSec, endSec);
            current = nextMidnight;
        }
    }

    private static int CalculateWindowOverlapSeconds(int workTimeStart, int workTimeEnd, DifferentialRule rule)
    {
        if (rule.IsAllDay)
        {
            return workTimeEnd - workTimeStart;
        }

        int windowStartSeconds = SecondsOf(rule.WindowStart);
        int windowEndSeconds = SecondsOf(rule.WindowEnd);
        var windowSpansMidnight = windowStartSeconds >= windowEndSeconds;

        return windowSpansMidnight
            ? Overlap(workTimeStart, workTimeEnd, windowStartSeconds, NodaConstants.SecondsPerDay) + Overlap(workTimeStart, workTimeEnd, 0, windowEndSeconds)  // wraps midnight
            : Overlap(workTimeStart, workTimeEnd, windowStartSeconds, windowEndSeconds);  // same-day window
    }

    // A single DayScheduleMode decides which days a rule is active on — the modes are mutually
    // exclusive, so this is a straight switch rather than a chain of ANDed filters.
    private static bool RuleAppliesOn(DifferentialRule rule, LocalDate date, PipelineContext ctx)
        => rule.DayScheduleMode switch
        {
            DayScheduleMode.EveryDay => true,
            DayScheduleMode.DaysOfWeek => rule.DaysOfWeek.Contains(date.DayOfWeek),
            DayScheduleMode.ConsecutiveDayRange =>
                DayOfWeekRange.Contains(date.DayOfWeek, rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd),
            DayScheduleMode.SpecificDates => rule.SpecificDates.Contains(date),
            DayScheduleMode.Holidays => ctx.HolidayCalendar?.IsHoliday(date) == true,
            _ => true,
        };

    private static int Overlap(int aStart, int aEnd, int bStart, int bEnd)
    => Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));

    private static int SecondsOf(LocalTime t) => t.Hour * 3600 + t.Minute * 60 + t.Second;
}
