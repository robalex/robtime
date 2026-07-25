using NodaTime;
using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Pure request-shape and business-rule validation — no DB access, so every rule here is unit
/// testable on its own. The service supplies the existing ranges for the overlap check rather than
/// this class querying for them.
/// </summary>
public static class PositionAssignmentValidator
{
    public static IDictionary<string, string[]> ValidateShape(LocalDate effectiveFrom, LocalDate? effectiveTo, decimal? rate)
    {
        var errors = new Dictionary<string, string[]>();

        if (!new DateRange(effectiveFrom, effectiveTo).IsWellFormed)
        {
            errors["effectiveTo"] = ["The end date cannot be before the start date."];
        }

        if (rate is < 0)
        {
            errors["rate"] = ["Rate cannot be negative."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> ValidateShape(CreatePositionAssignmentRequest request) =>
        ValidateShape(request.EffectiveFrom, request.EffectiveTo, request.Rate);

    public static IDictionary<string, string[]> ValidateShape(UpdatePositionAssignmentRequest request) =>
        ValidateShape(request.EffectiveFrom, request.EffectiveTo, request.Rate);

    /// <summary>
    /// Finds the first existing assignment the proposed range collides with, or null when it's free.
    ///
    /// An employee holds at most one position at a time (decided 2026-07-25), which is what makes
    /// <c>PipelineContext.FindEffective</c>'s "first match wins" resolution unambiguous — allow
    /// overlaps and which position applies to a punch becomes order-dependent, i.e. arbitrary.
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
