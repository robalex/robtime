using NodaTime;
using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Pure request-shape and business-rule validation — no DB access, so every rule here is unit
/// testable on its own. The service supplies the existing ranges for the overlap check, mirroring
/// PayRuleAssignmentValidator.
/// </summary>
public static class StateMinimumWageRequestValidator
{
    public static IDictionary<string, string[]> Validate(string state, LocalDate effectiveFrom, LocalDate? effectiveTo, decimal amount)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(state))
        {
            errors["state"] = ["State is required."];
        }

        if (!new DateRange(effectiveFrom, effectiveTo).IsWellFormed)
        {
            errors["effectiveTo"] = ["The end date cannot be before the start date."];
        }

        if (amount < 0)
        {
            errors["amount"] = ["Amount cannot be negative."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> Validate(CreateStateMinimumWageRequest request) =>
        Validate(request.State, request.EffectiveFrom, request.EffectiveTo, request.Amount);

    public static IDictionary<string, string[]> Validate(UpdateStateMinimumWageRequest request) =>
        Validate(request.State, request.EffectiveFrom, request.EffectiveTo, request.Amount);

    /// <summary>
    /// Finds the first existing row (same state) the proposed range collides with, or null when
    /// it's free. Two minimum-wage rows for the same state can't have overlapping windows, or a
    /// lookup as of a given date would be ambiguous.
    /// </summary>
    public static DateRange? FindConflict(DateRange proposed, IEnumerable<DateRange> existingForSameState)
    {
        foreach (var range in existingForSameState)
        {
            if (proposed.Overlaps(range))
            {
                return range;
            }
        }

        return null;
    }

    public static string DescribeConflict(DateRange conflict) =>
        conflict.To is null
            ? $"It overlaps a rate starting {conflict.From:yyyy-MM-dd} that is still in effect."
            : $"It overlaps a rate from {conflict.From:yyyy-MM-dd} to {conflict.To:yyyy-MM-dd}.";
}
