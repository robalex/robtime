using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Calculation.Premiums;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Pure request-shape validation and overlap-conflict detection — no DB access, so every rule here
/// is unit testable on its own. The service supplies the existing ranges for the overlap check
/// (mirrors PayRuleAssignmentValidator).
/// </summary>
public static class ClientPremiumPolicyRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateClientPremiumPolicyRequest request) =>
        Validate(request.PremiumCode, request.EffectiveFrom, request.EffectiveTo);

    public static IDictionary<string, string[]> Validate(UpdateClientPremiumPolicyRequest request) =>
        Validate(request.PremiumCode, request.EffectiveFrom, request.EffectiveTo);

    private static IDictionary<string, string[]> Validate(
        string premiumCode, LocalDate effectiveFrom, LocalDate? effectiveTo)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(premiumCode))
        {
            errors["premiumCode"] = ["Premium code is required."];
        }
        else if (!PremiumRegistry.AllCodes.Contains(premiumCode))
        {
            errors["premiumCode"] = [$"'{premiumCode}' is not a registered premium rule code."];
        }

        if (!new DateRange(effectiveFrom, effectiveTo).IsWellFormed)
        {
            errors["effectiveTo"] = ["The end date cannot be before the start date."];
        }

        return errors;
    }

    /// <summary>
    /// Finds the first existing policy the proposed range collides with, or null when it's free.
    ///
    /// A client has at most one waiver policy per premium code at a time — GetWaiverPolicyOverridesAt
    /// only picks the latest-EffectiveFrom row as a defensive fallback (PipelineContext.cs's own doc
    /// comment), and this is what's supposed to make that fallback unreachable in practice.
    /// </summary>
    public static DateRange? FindConflict(DateRange proposed, IEnumerable<DateRange> existing)
    {
        DateRange? conflict = null;
        foreach (var range in existing)
        {
            if (proposed.Overlaps(range))
            {
                conflict = range;
                break;
            }
        }

        return conflict;
    }

    /// <summary>Human-readable description of a clash, for the Conflict response detail.</summary>
    public static string DescribeConflict(DateRange conflict) =>
        conflict.To is null
            ? $"It overlaps a policy for this premium code starting {conflict.From:yyyy-MM-dd} that is still in effect."
            : $"It overlaps a policy for this premium code from {conflict.From:yyyy-MM-dd} to {conflict.To:yyyy-MM-dd}.";
}
