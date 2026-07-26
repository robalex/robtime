using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape/consistency validation — no DB access, so this is unit-testable
/// on its own.</summary>
public static class DifferentialRuleRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateDifferentialRuleRequest request) =>
        Validate(request.Code, request.AdjustmentValue, request.MinHoursInWindow, request.MinHoursInRange);

    public static IDictionary<string, string[]> Validate(UpdateDifferentialRuleRequest request) =>
        Validate(request.Code, request.AdjustmentValue, request.MinHoursInWindow, request.MinHoursInRange);

    private static IDictionary<string, string[]> Validate(
        string code, decimal adjustmentValue, decimal minHoursInWindow, decimal minHoursInRange)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code))
        {
            errors["code"] = ["Code is required."];
        }

        if (adjustmentValue < 0)
        {
            errors["adjustmentValue"] = ["Adjustment value cannot be negative."];
        }

        if (minHoursInWindow < 0)
        {
            errors["minHoursInWindow"] = ["Minimum hours in window cannot be negative."];
        }

        if (minHoursInRange < 0)
        {
            errors["minHoursInRange"] = ["Minimum hours in range cannot be negative."];
        }

        return errors;
    }

    /// <summary>
    /// Validates the fully-resolved <see cref="DifferentialRule"/> (mirrors
    /// PayRuleRequestValidator.ValidateConsistency) rather than the raw request, since which fields
    /// matter depends on DayScheduleMode. Mirrors PipelineContext's own
    /// ConsecutiveDayRange-must-span-two-days check (TimeCalculation/Pipeline/PipelineContext.cs) —
    /// duplicated here so a bad rule is rejected at the API boundary instead of only surfacing the
    /// first time the pipeline runs for an affected employee.
    /// </summary>
    public static IDictionary<string, string[]> ValidateConsistency(DifferentialRule rule)
    {
        var errors = new Dictionary<string, string[]>();
        switch (rule.DayScheduleMode)
        {
            case DayScheduleMode.DaysOfWeek when rule.DaysOfWeek.Count == 0:
                errors["daysOfWeek"] = ["At least one day of week is required for the DaysOfWeek mode."];
                break;
            case DayScheduleMode.ConsecutiveDayRange when rule.DayOfWeekRangeStart == rule.DayOfWeekRangeEnd:
                errors["dayOfWeekRangeEnd"] =
                    ["Range start and end can't be the same day — use the DaysOfWeek mode for a single day."];
                break;
            case DayScheduleMode.SpecificDates when rule.SpecificDates.Count == 0:
                errors["specificDates"] = ["At least one date is required for the SpecificDates mode."];
                break;
        }

        return errors;
    }
}
