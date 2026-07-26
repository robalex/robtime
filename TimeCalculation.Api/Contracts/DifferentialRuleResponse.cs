using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public sealed record DifferentialRuleResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string Code { get; init; }
    public required DayScheduleMode DayScheduleMode { get; init; }

    public required HashSet<IsoDayOfWeek> DaysOfWeek { get; init; }
    public required IsoDayOfWeek DayOfWeekRangeStart { get; init; }
    public required IsoDayOfWeek DayOfWeekRangeEnd { get; init; }
    public required HashSet<LocalDate> SpecificDates { get; init; }

    public required LocalTime WindowStart { get; init; }
    public required LocalTime WindowEnd { get; init; }
    public required bool IsAllDay { get; init; }

    public required DifferentialAdjustmentType AdjustmentType { get; init; }
    public required decimal AdjustmentValue { get; init; }

    public required decimal MinHoursInWindow { get; init; }
    public required decimal MinHoursInRange { get; init; }

    public string? ExclusivityGroup { get; init; }

    public static DifferentialRuleResponse FromEntity(DifferentialRule rule) => new()
    {
        Id = rule.Id,
        ClientId = rule.ClientId,
        Code = rule.Code,
        DayScheduleMode = rule.DayScheduleMode,
        DaysOfWeek = rule.DaysOfWeek.ToHashSet(),
        DayOfWeekRangeStart = rule.DayOfWeekRangeStart,
        DayOfWeekRangeEnd = rule.DayOfWeekRangeEnd,
        SpecificDates = rule.SpecificDates.ToHashSet(),
        WindowStart = rule.WindowStart,
        WindowEnd = rule.WindowEnd,
        IsAllDay = rule.IsAllDay,
        AdjustmentType = rule.AdjustmentType,
        AdjustmentValue = rule.AdjustmentValue,
        MinHoursInWindow = rule.MinHoursInWindow,
        MinHoursInRange = rule.MinHoursInRange,
        ExclusivityGroup = rule.ExclusivityGroup,
    };
}
