namespace TimeCalculation.Model;

/// <summary>
/// How a DifferentialRule selects which calendar days it is active on. Exactly one mode applies —
/// the modes are mutually exclusive, so configuring one never requires touching the others' fields.
/// The rule's time-of-day window (WindowStart/WindowEnd) is orthogonal and applies on top of any mode.
/// </summary>
public enum DayScheduleMode
{
    /// <summary>Active every day (default) — no day restriction.</summary>
    EveryDay,

    /// <summary>Active on the specific weekdays in DifferentialRule.DaysOfWeek (e.g. Mon/Wed/Fri).</summary>
    DaysOfWeek,

    /// <summary>
    /// Active across the consecutive weekday span DayOfWeekRangeStart..DayOfWeekRangeEnd (e.g.
    /// Thursday..Tuesday), which may wrap past Sunday. The only mode that supports MinHoursInRange.
    /// </summary>
    ConsecutiveDayRange,

    /// <summary>Active on the explicit dates in DifferentialRule.SpecificDates (e.g. a corporate holiday).</summary>
    SpecificDates,

    /// <summary>Active on dates flagged by the pipeline context's shared HolidayCalendar.</summary>
    Holidays,
}
