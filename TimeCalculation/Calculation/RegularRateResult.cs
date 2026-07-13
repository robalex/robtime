namespace TimeCalculation.Calculation;

/// <summary>
/// The FLSA regular rate of pay for one workweek, with the components that produced it retained
/// for audit.  RROP = (straight-time earnings + fixed-hours earnings + non-discretionary bonuses
/// + differentials) ÷ hours.
/// </summary>
public record RegularRateResult
{
    public decimal StraightTimeEarnings { get; init; }
    /// <summary>Pay from FixedHours entries with CountsTowardRegularRate = true, at minimum wage.</summary>
    public decimal FixedHoursEarnings { get; init; }
    public decimal NonDiscretionaryBonuses { get; init; }
    public decimal Differentials { get; init; }
    public decimal TotalHours { get; init; }
    public decimal RegularRate { get; init; }
}
