using NodaTime;

namespace TimeCalculation.Model;

public record WorkDay
{
    public LocalDate Date { get; init; }
    public IReadOnlyList<Shift> Shifts { get; init; } = [];

    /// <summary>
    /// 1-based count of consecutive workdays in the current workweek.
    /// Set during workweek grouping (Stage 10). Used for 7th-consecutive-day OT rules.
    /// </summary>
    public int ConsecutiveDayNumber { get; init; }

    public decimal TotalHours => Shifts.Sum(s => s.TotalHours);
}
