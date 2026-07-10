namespace TimeCalculation.Calculation;

/// <summary>
/// The FLSA regular rate of pay for one workweek, with the components that produced it retained
/// for audit.  RROP = (straight-time earnings + non-discretionary bonuses + differentials) ÷ hours.
/// </summary>
public record RegularRateResult
{
    public decimal StraightTimeEarnings { get; init; }
    public decimal NonDiscretionaryBonuses { get; init; }
    public decimal Differentials { get; init; }
    public decimal TotalHours { get; init; }
    public decimal RegularRate { get; init; }
}
