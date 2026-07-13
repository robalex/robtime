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

    /// <summary>
    /// The rule's own configured value that produced Amount — a UI can show exactly what was
    /// applied without re-deriving it: $/hr for FlatPerHour, the fractional multiplier of the
    /// pair's own rate for Multiplier (e.g. 0.10 for 10%), or the flat lump sum for FixedBonus.
    /// </summary>
    public decimal AdjustmentValue { get; init; }
}
