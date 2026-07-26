namespace TimeCalculation.Api;

/// <summary>
/// A jurisdiction preset for creating a <c>PayRule</c> (UI_PLAN.md §6 Rule 3: "templates before
/// fields" — creating a rule must start here, and every field stays editable afterward). Only
/// presets what's already implemented and tested elsewhere in this codebase — the six premium rule
/// classes under <c>TimeCalculation.Calculation.Premiums</c> and the daily-OT/7th-day overtime
/// config <c>OvertimeRuleFactory</c> already knows how to run. It deliberately does NOT assert
/// state-specific overtime thresholds beyond California's (already used by
/// <c>OvertimeRuleFactory</c>/<c>CaliforniaOvertimeRule</c> and mirrored in <c>DevSeeder</c>) —
/// Colorado/Oregon/Washington/Puerto Rico daily-OT rules are real but not yet encoded anywhere in
/// this engine, and PLAN.md §6 is explicit that this category of legal detail needs a state-by-state
/// check before locking in. Guessing at a threshold here would silently produce wrong pay.
/// </summary>
public sealed record PayRuleTemplate
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Bumped whenever a template's presets change, so PayRule.TemplateVersion can record
    /// which snapshot a rule was created from (UI_PLAN.md §6 Rule 3's template-drift tracking).</summary>
    public required int Version { get; init; }

    public required HashSet<string> ActivePremiumCodes { get; init; }
    public required bool HasDailyOvertime { get; init; }
    public required decimal DailyOvertimeThresholdHours { get; init; }
    public required decimal DailyDoubletimeThresholdHours { get; init; }
    public required bool HasSeventhDayRule { get; init; }
    public required decimal WeeklyOvertimeThresholdHours { get; init; }
}

public static class PayRuleTemplateRegistry
{
    private static readonly IReadOnlyList<PayRuleTemplate> Templates =
    [
        new PayRuleTemplate
        {
            Code = "federal-standard",
            Name = "Federal Standard",
            Description = "Baseline FLSA rules: weekly overtime past 40 hours, no state premiums. " +
                           "The right starting point for any client with no state-specific requirements.",
            Version = 1,
            ActivePremiumCodes = [],
            HasDailyOvertime = false,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = false,
            WeeklyOvertimeThresholdHours = 40,
        },
        new PayRuleTemplate
        {
            Code = "california",
            Name = "California",
            Description = "Daily overtime past 8 hours, double-time past 12, 7th-consecutive-day " +
                           "overtime, and the CA meal/rest premiums.",
            Version = 1,
            ActivePremiumCodes = ["CA_MEAL", "CA_REST"],
            HasDailyOvertime = true,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = true,
            WeeklyOvertimeThresholdHours = 40,
        },
        new PayRuleTemplate
        {
            Code = "colorado",
            Name = "Colorado",
            Description = "Enables the CO paid rest break premium. Colorado's daily-overtime rule " +
                           "isn't encoded in this engine yet — review overtime settings before " +
                           "relying on this template for a Colorado client.",
            Version = 1,
            ActivePremiumCodes = ["CO_REST"],
            HasDailyOvertime = false,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = false,
            WeeklyOvertimeThresholdHours = 40,
        },
        new PayRuleTemplate
        {
            Code = "oregon",
            Name = "Oregon",
            Description = "Enables the OR meal-break premium. Federal overtime rules apply otherwise.",
            Version = 1,
            ActivePremiumCodes = ["OR_MEAL"],
            HasDailyOvertime = false,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = false,
            WeeklyOvertimeThresholdHours = 40,
        },
        new PayRuleTemplate
        {
            Code = "washington",
            Name = "Washington",
            Description = "Enables the WA meal-break premium. Federal overtime rules apply otherwise.",
            Version = 1,
            ActivePremiumCodes = ["WA_MEAL"],
            HasDailyOvertime = false,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = false,
            WeeklyOvertimeThresholdHours = 40,
        },
        new PayRuleTemplate
        {
            Code = "puerto-rico",
            Name = "Puerto Rico",
            Description = "Enables the PR meal-break premium. Federal overtime rules apply otherwise.",
            Version = 1,
            ActivePremiumCodes = ["PR_MEAL"],
            HasDailyOvertime = false,
            DailyOvertimeThresholdHours = 8,
            DailyDoubletimeThresholdHours = 12,
            HasSeventhDayRule = false,
            WeeklyOvertimeThresholdHours = 40,
        },
    ];

    public static IReadOnlyList<PayRuleTemplate> All => Templates;

    public static PayRuleTemplate? Find(string code) =>
        Templates.FirstOrDefault(t => t.Code == code);
}
