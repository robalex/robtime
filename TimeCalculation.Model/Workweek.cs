using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A single FLSA 168-hour workweek: the unit for weekly overtime and the regular-rate-of-pay
/// calculation.  Anchored on PayRule.WorkweekStartDay at midnight in the employee's timezone.
/// Independent of the pay period — a bi-weekly period contains exactly two of these, while
/// semi-monthly and monthly periods may straddle workweek boundaries.
/// </summary>
public record Workweek
{
    /// <summary>The anchor day (the WorkweekStartDay) this week begins on, in the employee's timezone.</summary>
    public LocalDate StartDate { get; init; }

    /// <summary>Midnight of StartDate in the employee's timezone; start of the 168-hour window.</summary>
    public Instant StartInstant { get; init; }

    public IReadOnlyList<WorkDay> Days { get; init; } = [];

    /// <summary>Exclusive end of the 168-hour window.</summary>
    public Instant EndInstant => StartInstant + Duration.FromDays(7);

    public decimal TotalHours => Days.Sum(d => d.TotalHours);
}
