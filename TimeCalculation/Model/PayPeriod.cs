using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A pay-cycle bucket.  Boundaries are calendar dates (in the employee's timezone); the actual
/// cost proration of workweeks that straddle a boundary is handled downstream, not here.
/// </summary>
public record PayPeriod
{
    public LocalDate Start { get; init; }        // inclusive
    public LocalDate End { get; init; }          // inclusive
    public PayPeriodFrequency Frequency { get; init; }

    public int LengthInDays => Period.DaysBetween(Start, End) + 1;

    public bool Contains(LocalDate date) => date >= Start && date <= End;
}
