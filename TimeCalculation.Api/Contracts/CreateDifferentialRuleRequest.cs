using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public record CreateDifferentialRuleRequest
{
    public required int ClientId { get; init; }
    public required string Code { get; init; }
    public required DayScheduleMode DayScheduleMode { get; init; }

    // HashSet, not IReadOnlySet — System.Text.Json can't deserialize an interface-typed collection
    // without a custom converter (same reasoning as PayRuleFieldsRequest.ActivePremiumCodes).
    public HashSet<IsoDayOfWeek>? DaysOfWeek { get; init; }

    public IsoDayOfWeek DayOfWeekRangeStart { get; init; }
    public IsoDayOfWeek DayOfWeekRangeEnd { get; init; }

    public HashSet<LocalDate>? SpecificDates { get; init; }

    public LocalTime WindowStart { get; init; }
    public LocalTime WindowEnd { get; init; }

    public required DifferentialAdjustmentType AdjustmentType { get; init; }
    public required decimal AdjustmentValue { get; init; }

    public decimal MinHoursInWindow { get; init; }
    public decimal MinHoursInRange { get; init; }

    public string? ExclusivityGroup { get; init; }
}
