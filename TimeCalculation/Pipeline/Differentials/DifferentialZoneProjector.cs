using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// Projects where a DifferentialRule *could* apply over a date window — independent of any actual
/// punches, unlike DifferentialApplier which only ever sees qualifying worked time. Powers the
/// differential sandbox's calendar view ("draw every rule's active periods as blocks"). Reuses the
/// exact day-membership test (DifferentialDaySchedule.AppliesOn) and range-occurrence span
/// arithmetic (ContinuousRangeQualifyingHoursCalculator.OccurrenceSpan) the real pipeline uses, so
/// the sandbox can never show a zone the engine wouldn't actually honor.
/// </summary>
public static class DifferentialZoneProjector
{
    public static IReadOnlyList<DifferentialZone> Project(
        DifferentialRule rule, LocalDate from, LocalDate to, PipelineContext ctx)
        => rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange
            ? ProjectRange(rule, from, to, ctx.EmployeeTimeZone)
            : ProjectPerDay(rule, from, to, ctx).ToList();

    // Mirrors ContinuousRangeQualifyingHoursCalculator: walk occurrence anchors one week at a time,
    // starting a week early so a span beginning just before `from` (but still overlapping it) isn't
    // missed, and stopping once a span starts at or after the window's end.
    private static IReadOnlyList<DifferentialZone> ProjectRange(
        DifferentialRule rule, LocalDate from, LocalDate to, DateTimeZone zone)
    {
        var rangeLengthDays = DayOfWeekRange.Length(rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd);
        var windowStart = from.AtMidnight().InZoneLeniently(zone).ToInstant();
        var windowEnd = to.PlusDays(1).AtMidnight().InZoneLeniently(zone).ToInstant();

        var zones = new List<DifferentialZone>();
        var anchor = DayOfWeekRange.OccurrenceAnchor(from, rule.DayOfWeekRangeStart).PlusDays(-7);
        while (true)
        {
            var span = ContinuousRangeQualifyingHoursCalculator.OccurrenceSpan(rule, anchor, rangeLengthDays, zone);
            if (span.Start >= windowEnd)
            {
                break;
            }
            if (span.End > windowStart)
            {
                zones.Add(new DifferentialZone { Code = rule.Code, Start = span.Start, End = span.End });
            }
            anchor = anchor.PlusDays(7);
        }
        return zones;
    }

    // Every other mode: WindowStart/WindowEnd is a per-day window applied independently on each
    // active day — mirrors PerDayQualifyingHoursCalculator.CalculateWindowOverlapSeconds's own
    // wrap-handling, just projecting the window's own two pieces rather than their overlap with a
    // worked interval.
    private static IEnumerable<DifferentialZone> ProjectPerDay(
        DifferentialRule rule, LocalDate from, LocalDate to, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        for (var date = from; date <= to; date = date.PlusDays(1))
        {
            if (!DifferentialDaySchedule.AppliesOn(rule, date, ctx.HolidayCalendar))
            {
                continue;
            }

            var midnight = date.AtMidnight().InZoneLeniently(zone).ToInstant();
            var nextMidnight = date.PlusDays(1).AtMidnight().InZoneLeniently(zone).ToInstant();

            if (rule.IsAllDay)
            {
                yield return new DifferentialZone { Code = rule.Code, Start = midnight, End = nextMidnight };
                continue;
            }

            var windowStart = date.At(rule.WindowStart).InZoneLeniently(zone).ToInstant();
            var windowEnd = date.At(rule.WindowEnd).InZoneLeniently(zone).ToInstant();

            if (rule.WindowStart >= rule.WindowEnd)
            {
                // Wraps midnight: an early-morning portion attributed to `date` (the tail of the
                // previous day's window) plus an evening portion continuing into the next day.
                if (windowEnd > midnight)
                {
                    yield return new DifferentialZone { Code = rule.Code, Start = midnight, End = windowEnd };
                }
                yield return new DifferentialZone { Code = rule.Code, Start = windowStart, End = nextMidnight };
            }
            else
            {
                yield return new DifferentialZone { Code = rule.Code, Start = windowStart, End = windowEnd };
            }
        }
    }
}
