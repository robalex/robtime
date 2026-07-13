using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// A state-specific meal/rest premium rule.  Resolved from PayRule.ActivePremiumCodes by the
/// registry.  Adding a state = implementing this interface and registering the code.
///
/// Rules act on a ShiftAnalysis (worked hours + classified break/lunch gaps) rather than the raw
/// Shift — every rule today only needs that derived view, and PremiumApplier computes it once per
/// shift and passes the same instance to both methods, instead of every rule rebuilding it.
/// </summary>
public interface IPremiumRule
{
    string Code { get; }                 // "CA_MEAL", "CO_REST", ...
    Jurisdiction Jurisdiction { get; }
    WaiverPolicy WaiverPolicy { get; }

    /// <summary>Whether this rule is relevant to the shift at all (e.g. shift long enough to owe a meal).</summary>
    bool Applies(ShiftAnalysis analysis, PremiumContext ctx);

    /// <summary>Evaluates the shift, producing the premium (or a zero result explaining why none is owed).</summary>
    PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx);
}
