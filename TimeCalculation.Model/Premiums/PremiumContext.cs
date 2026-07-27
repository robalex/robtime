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

    /// <summary>
    /// Client-asserted <see cref="WaiverPolicy"/> overrides, keyed by <c>PremiumCode</c>, resolved
    /// as of the shift's date (see PipelineContext.GetWaiverPolicyOverridesAt). A code absent here
    /// has no client override — PremiumRuleBase.Resolve falls back to the rule's own built-in
    /// default in that case.
    /// </summary>
    public IReadOnlyDictionary<string, WaiverPolicy> WaiverPolicyOverrides { get; init; } =
        new Dictionary<string, WaiverPolicy>();
}
