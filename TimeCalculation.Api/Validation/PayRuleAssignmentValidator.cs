using NodaTime;
using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Pure request-shape and business-rule validation — no DB access, so every rule here is unit
/// testable on its own. The service supplies the existing ranges for the overlap check rather than
/// this class querying for them. Mirrors PositionAssignmentValidator; a PayRuleAssignment is the
/// same (thing, from, to?) shape minus the per-assignment rate override.
/// </summary>
public static class PayRuleAssignmentValidator
{
    public static IDictionary<string, string[]> ValidateShape(LocalDate effectiveFrom, LocalDate? effectiveTo)
    {
        var errors = new Dictionary<string, string[]>();

        if (!new DateRange(effectiveFrom, effectiveTo).IsWellFormed)
        {
            errors["effectiveTo"] = ["The end date cannot be before the start date."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> ValidateShape(CreatePayRuleAssignmentRequest request) =>
        ValidateShape(request.EffectiveFrom, request.EffectiveTo);

    public static IDictionary<string, string[]> ValidateShape(UpdatePayRuleAssignmentRequest request) =>
        ValidateShape(request.EffectiveFrom, request.EffectiveTo);

    /// <summary>
    /// Finds the first existing assignment the proposed range collides with, or null when it's free.
    ///
    /// An employee is governed by at most one pay rule at a time, same as position (decided
    /// 2026-07-25) — <c>PipelineContext.GetRuleAt</c>'s "first match wins" resolution is only
    /// unambiguous if assignments never overlap.
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
            ? $"It overlaps an assignment starting {conflict.From:yyyy-MM-dd} that is still in effect."
            : $"It overlaps an assignment from {conflict.From:yyyy-MM-dd} to {conflict.To:yyyy-MM-dd}.";
}
