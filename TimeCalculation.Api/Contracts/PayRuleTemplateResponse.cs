using TimeCalculation.Api;

namespace TimeCalculation.Api.Contracts;

public sealed record PayRuleTemplateResponse
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Version { get; init; }
    public required HashSet<string> ActivePremiumCodes { get; init; }
    public required bool HasDailyOvertime { get; init; }
    public required decimal DailyOvertimeThresholdHours { get; init; }
    public required decimal DailyDoubletimeThresholdHours { get; init; }
    public required bool HasSeventhDayRule { get; init; }
    public required decimal WeeklyOvertimeThresholdHours { get; init; }

    public static PayRuleTemplateResponse FromTemplate(PayRuleTemplate template) => new()
    {
        Code = template.Code,
        Name = template.Name,
        Description = template.Description,
        Version = template.Version,
        ActivePremiumCodes = template.ActivePremiumCodes,
        HasDailyOvertime = template.HasDailyOvertime,
        DailyOvertimeThresholdHours = template.DailyOvertimeThresholdHours,
        DailyDoubletimeThresholdHours = template.DailyDoubletimeThresholdHours,
        HasSeventhDayRule = template.HasSeventhDayRule,
        WeeklyOvertimeThresholdHours = template.WeeklyOvertimeThresholdHours,
    };
}
