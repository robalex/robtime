using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A time-based pay differential: extra compensation for working certain days/times
/// (night shift, weekend, holiday).  A simplified structured recurrence stands in for a full
/// RFC 5545 RRULE — a single DayScheduleMode picks which days the rule is active on, plus an
/// employee-local time-of-day window. Differentials are additional compensation for hours worked,
/// so they feed the regular rate.
/// </summary>
public class DifferentialRule
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;

    // ── Day schedule: exactly one mode selects the active days; the others' fields are ignored. ──
    public DayScheduleMode DayScheduleMode { get; set; } = DayScheduleMode.EveryDay;

    /// <summary>Active weekdays when DayScheduleMode == DaysOfWeek (e.g. Mon/Wed/Fri). Order-independent.</summary>
    public IReadOnlySet<IsoDayOfWeek> DaysOfWeek { get; set; } = new HashSet<IsoDayOfWeek>();

    /// <summary>
    /// Inclusive consecutive weekday span when DayScheduleMode == ConsecutiveDayRange, e.g. Thursday
    /// through Tuesday for a long-weekend differential. May wrap past Sunday the same way
    /// WindowStart/WindowEnd wraps past midnight (Start later in the week than End crosses the week
    /// boundary).
    /// </summary>
    public IsoDayOfWeek DayOfWeekRangeStart { get; set; }
    public IsoDayOfWeek DayOfWeekRangeEnd { get; set; }

    /// <summary>
    /// Explicit active dates when DayScheduleMode == SpecificDates (e.g. a corporate holiday that
    /// isn't in the shared HolidayCalendar). Rule-specific, so different rules can recognize
    /// different one-off dates.
    /// </summary>
    public IReadOnlySet<LocalDate> SpecificDates { get; set; } = new HashSet<LocalDate>();

    // ── Window (employee-local time-of-day). WindowStart == WindowEnd means all day. ──
    /// <summary>Window may wrap past midnight (e.g. 18:00–06:00 for a night differential).</summary>
    public LocalTime WindowStart { get; set; }
    public LocalTime WindowEnd { get; set; }

    // ── Adjustment ──
    public DifferentialAdjustmentType AdjustmentType { get; set; }
    public decimal AdjustmentValue { get; set; }

    // ── Qualification ──
    /// <summary>Minimum hours worked inside the window (within a single shift) to earn the differential.</summary>
    public decimal MinHoursInWindow { get; set; }

    /// <summary>
    /// Minimum qualifying hours summed across one occurrence of the consecutive day range to earn
    /// the differential — the same qualifying hours MinHoursInWindow measures, but totaled over the
    /// range span (e.g. one Thursday..Tuesday block) rather than one shift. 0 (the default) means no
    /// range threshold. Only meaningful when DayScheduleMode == ConsecutiveDayRange; each occurrence
    /// of the range is judged independently. Leave MinHoursInWindow at 0 when using this, so
    /// individual shifts aren't dropped before their hours can be summed across the range.
    /// </summary>
    public decimal MinHoursInRange { get; set; }

    // ── Stacking ──
    /// <summary>
    /// Optional exclusivity group.  Differentials sharing the same non-empty group are mutually
    /// exclusive on a shift — only the highest-amount one applies.  Null/empty (the default) means
    /// the differential always stacks with every other differential (e.g. overnight + holiday both pay).
    /// </summary>
    public string? ExclusivityGroup { get; set; }

    public bool IsAllDay => WindowStart == WindowEnd;
}
