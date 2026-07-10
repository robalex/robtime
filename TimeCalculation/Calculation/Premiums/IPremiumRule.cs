using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// A state-specific meal/rest premium rule.  Resolved from PayRule.ActivePremiumCodes by the
/// registry.  Adding a state = implementing this interface and registering the code.
/// </summary>
public interface IPremiumRule
{
    string Code { get; }                 // "CA_MEAL", "CO_REST", ...
    Jurisdiction Jurisdiction { get; }
    WaiverPolicy WaiverPolicy { get; }

    /// <summary>Whether this rule is relevant to the shift at all (e.g. shift long enough to owe a meal).</summary>
    bool Applies(Shift shift, PremiumContext ctx);

    /// <summary>Evaluates the shift, producing the premium (or a zero result explaining why none is owed).</summary>
    PremiumResult Calculate(Shift shift, PremiumContext ctx);
}
