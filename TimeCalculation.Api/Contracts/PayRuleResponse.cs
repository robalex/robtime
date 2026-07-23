using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Contracts;

public sealed record PayRuleResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? TemplateCode { get; init; }
    public int? TemplateVersion { get; init; }
    public required int RuleFamilyId { get; init; }
    public required int Version { get; init; }
    public required PayRuleStatus Status { get; init; }
    public LocalDate? EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }

    public required decimal PunchPairResetHours { get; init; }
    public required decimal MaxShiftLengthHours { get; init; }
    public required decimal DistanceBetweenShiftsHours { get; init; }
    public required int ExpectedBreakLengthMinutes { get; init; }
    public required int ExpectedLunchLengthMinutes { get; init; }

    public required RoundingStrategy RoundingStrategy { get; init; }
    public required int RoundingIntervalMinutes { get; init; }
    public required int RoundingGraceMinutes { get; init; }

    public required ShiftDateStrategy ShiftDateStrategy { get; init; }
    public required IsoDayOfWeek WorkweekStartDay { get; init; }

    // HashSet, not IReadOnlySet — see PayRuleFieldsRequest's comment on the same fields; a .NET
    // client (this project's own integration tests included) can't deserialize an interface-typed
    // collection with System.Text.Json's default converters.
    public required HashSet<string> ActivePremiumCodes { get; init; }
    public required HashSet<string> ActiveDifferentialCodes { get; init; }

    public required decimal WeeklyOvertimeThresholdHours { get; init; }
    public required bool HasDailyOvertime { get; init; }
    public required decimal DailyOvertimeThresholdHours { get; init; }
    public required decimal DailyDoubletimeThresholdHours { get; init; }
    public required bool HasSeventhDayRule { get; init; }

    public static PayRuleResponse FromEntity(PayRule payRule) => new()
    {
        Id = payRule.Id,
        ClientId = payRule.ClientId,
        Name = payRule.Name,
        Description = payRule.Description,
        TemplateCode = payRule.TemplateCode,
        TemplateVersion = payRule.TemplateVersion,
        RuleFamilyId = payRule.RuleFamilyId,
        Version = payRule.Version,
        Status = payRule.Status,
        EffectiveFrom = payRule.EffectiveFrom,
        EffectiveTo = payRule.EffectiveTo,
        PunchPairResetHours = payRule.PunchPairResetHours,
        MaxShiftLengthHours = payRule.MaxShiftLengthHours,
        DistanceBetweenShiftsHours = payRule.DistanceBetweenShiftsHours,
        ExpectedBreakLengthMinutes = payRule.ExpectedBreakLengthMinutes,
        ExpectedLunchLengthMinutes = payRule.ExpectedLunchLengthMinutes,
        RoundingStrategy = payRule.RoundingRule.RoundingStrategy,
        RoundingIntervalMinutes = payRule.RoundingRule.RoundingIntervalMinutes,
        RoundingGraceMinutes = payRule.RoundingRule.RoundingGraceMinutes,
        ShiftDateStrategy = payRule.ShiftDateStrategy,
        WorkweekStartDay = payRule.WorkweekStartDay,
        ActivePremiumCodes = payRule.ActivePremiumCodes.ToHashSet(),
        ActiveDifferentialCodes = payRule.ActiveDifferentialCodes.ToHashSet(),
        WeeklyOvertimeThresholdHours = payRule.OvertimeRule.WeeklyOvertimeThresholdHours,
        HasDailyOvertime = payRule.OvertimeRule.HasDailyOvertime,
        DailyOvertimeThresholdHours = payRule.OvertimeRule.DailyOvertimeThresholdHours,
        DailyDoubletimeThresholdHours = payRule.OvertimeRule.DailyDoubletimeThresholdHours,
        HasSeventhDayRule = payRule.OvertimeRule.HasSeventhDayRule,
    };
}
