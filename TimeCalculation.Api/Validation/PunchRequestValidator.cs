using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class PunchRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePunchRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Kind == PunchKind.FixedDollar && request.Amount is null)
        {
            errors["amount"] = ["Amount is required for FixedDollar punches."];
        }

        if (request.Kind == PunchKind.FixedHours && request.Hours is null)
        {
            errors["hours"] = ["Hours is required for FixedHours punches."];
        }

        return errors;
    }

    /// <summary>
    /// Same FixedDollar/FixedHours rule as <see cref="Validate"/>, but run against the fully-merged
    /// Punch (existing + request applied by PunchRequestMapper) rather than the raw update request —
    /// same reasoning as PayRuleRequestValidator.ValidateConsistency: a request that only changes
    /// Kind (and omits Amount/Hours, relying on partial-patch semantics to keep whatever the punch
    /// already had) can still produce an invalid combination that only the merged result reveals.
    /// </summary>
    public static IDictionary<string, string[]> ValidateConsistency(Punch punch)
    {
        var errors = new Dictionary<string, string[]>();
        if (punch.Kind == PunchKind.FixedDollar && punch.Amount is null)
        {
            errors["amount"] = ["Amount is required for FixedDollar punches."];
        }

        if (punch.Kind == PunchKind.FixedHours && punch.Hours is null)
        {
            errors["hours"] = ["Hours is required for FixedHours punches."];
        }

        return errors;
    }
}
