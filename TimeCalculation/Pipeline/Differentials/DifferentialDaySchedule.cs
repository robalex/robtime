using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// The single definition of "is this DifferentialRule active on this date" — a straight switch over
/// DayScheduleMode rather than a chain of ANDed filters, since the modes are mutually exclusive.
/// Shared by PerDayQualifyingHoursCalculator (which pairs this with a time-of-day window to compute
/// worked hours) and DifferentialZoneProjector (which uses it alone to project where a rule *could*
/// apply, independent of any punches) — both need to agree on day membership, so this is the one
/// place that decides it.
/// </summary>
public static class DifferentialDaySchedule
{
    public static bool AppliesOn(DifferentialRule rule, LocalDate date, HolidayCalendar? holidayCalendar)
        => rule.DayScheduleMode switch
        {
            DayScheduleMode.EveryDay => true,
            DayScheduleMode.DaysOfWeek => rule.DaysOfWeek.Contains(date.DayOfWeek),
            DayScheduleMode.ConsecutiveDayRange =>
                DayOfWeekRange.Contains(date.DayOfWeek, rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd),
            DayScheduleMode.SpecificDates => rule.SpecificDates.Contains(date),
            DayScheduleMode.Holidays => holidayCalendar?.IsHoliday(date) == true,
            _ => true,
        };
}
