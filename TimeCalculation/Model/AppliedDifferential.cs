namespace TimeCalculation.Model;

/// <summary>
/// The realized result of a DifferentialRule against a shift: how many qualifying hours were
/// worked in the window and the dollar amount earned.  Attached to Shift by Stage 8 and consumed
/// by the regular-rate calculation.
/// </summary>
public record AppliedDifferential
{
    public string Code { get; init; } = string.Empty;
    public decimal Hours { get; init; }    // qualifying hours in window (informational for FixedBonus)
    public decimal Amount { get; init; }   // dollars added
    public DifferentialAdjustmentType AdjustmentType { get; init; }
}
