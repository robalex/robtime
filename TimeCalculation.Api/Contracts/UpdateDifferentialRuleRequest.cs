using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// No ClientId — PUT operates on an existing DifferentialRule identified by the route id, and which
/// client owns it isn't something a caller changes via update.
/// </summary>
public record UpdateDifferentialRuleRequest
{
    public required string Code { get; init; }
    public required DayScheduleMode DayScheduleMode { get; init; }

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
