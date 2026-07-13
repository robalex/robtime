using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A time-based pay differential: extra compensation for working certain days/times
/// (night shift, weekend, holiday).  A simplified structured recurrence stands in for a full
/// RFC 5545 RRULE — day-of-week and holiday filters plus an employee-local time-of-day window.
/// Differentials are additional compensation for hours worked, so they feed the regular rate.
/// </summary>
public class DifferentialRule
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;

    // ── Recurrence filters (a day must satisfy all set filters for the rule to apply) ──
    /// <summary>Days the rule is active on. Empty = every day.</summary>
    public IReadOnlySet<IsoDayOfWeek> DaysOfWeek { get; set; } = new HashSet<IsoDayOfWeek>();

    /// <summary>When true, the rule only applies on dates in the context's holiday calendar.</summary>
    public bool HolidaysOnly { get; set; }

    // ── Window (employee-local time-of-day). WindowStart == WindowEnd means all day. ──
    /// <summary>Window may wrap past midnight (e.g. 18:00–06:00 for a night differential).</summary>
    public LocalTime WindowStart { get; set; }
    public LocalTime WindowEnd { get; set; }

    // ── Adjustment ──
    public DifferentialAdjustmentType AdjustmentType { get; set; }
    public decimal AdjustmentValue { get; set; }

    // ── Qualification ──
    /// <summary>Minimum hours worked inside the window (across the shift) to earn the differential.</summary>
    public decimal MinHoursInWindow { get; set; }

    // ── Stacking ──
    /// <summary>
    /// Optional exclusivity group.  Differentials sharing the same non-empty group are mutually
    /// exclusive on a shift — only the highest-amount one applies.  Null/empty (the default) means
    /// the differential always stacks with every other differential (e.g. overnight + holiday both pay).
    /// </summary>
    public string? ExclusivityGroup { get; set; }

    public bool IsAllDay => WindowStart == WindowEnd;
}
