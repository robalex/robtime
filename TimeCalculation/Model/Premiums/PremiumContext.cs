namespace TimeCalculation.Model.Premiums;

/// <summary>
/// Inputs a premium rule needs beyond the shift itself: the regular rate of pay (premiums are
/// "one hour at the regular rate", and Puerto Rico's meal premium is at the overtime rate), and
/// any overrides asserted against this shift's premium occurrence.
/// </summary>
public record PremiumContext
{
    public decimal RegularRate { get; init; }
    public decimal OvertimeRate => RegularRate * 1.5m;
    public IReadOnlyList<OverrideKind> Overrides { get; init; } = [];
}
