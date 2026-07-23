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

    /// <summary>Short human-readable label for a premium-selection UI, e.g. "California Meal
    /// Premium" — not mechanically derived from Code, since that mapping isn't 1:1 obvious.</summary>
    string Name { get; }

    /// <summary>One or two sentences: what triggers it and what it pays. Sourced from this rule's
    /// own class doc comment, which is the actual legal citation/reasoning — kept in sync by hand,
    /// not generated, since Description is meant to be read by someone configuring a client's pay
    /// rules without opening the source.</summary>
    string Description { get; }

    Jurisdiction Jurisdiction { get; }
    WaiverPolicy WaiverPolicy { get; }

    /// <summary>Whether this rule is relevant to the shift at all (e.g. shift long enough to owe a meal).</summary>
    bool Applies(ShiftAnalysis analysis, PremiumContext ctx);

    /// <summary>Evaluates the shift, producing the premium (or a zero result explaining why none is owed).</summary>
    PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx);
}
