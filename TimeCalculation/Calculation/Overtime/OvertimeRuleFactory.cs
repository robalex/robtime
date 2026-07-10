using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// Selects the overtime strategy for a PayRule's OvertimeRule config: federal weekly-only when no
/// daily or seventh-day tiers are enabled, otherwise the daily/weekly/7th-day rule with the
/// configured thresholds and flags.
/// </summary>
public static class OvertimeRuleFactory
{
    public static IOvertimeRule FromConfig(OvertimeRule config)
    {
        if (!config.HasDailyOvertime && !config.HasSeventhDayRule)
            return new FederalOvertimeRule(config.WeeklyOvertimeThresholdHours);

        return new CaliforniaOvertimeRule(
            config.DailyOvertimeThresholdHours,
            config.DailyDoubletimeThresholdHours,
            config.WeeklyOvertimeThresholdHours,
            applyDaily: config.HasDailyOvertime,
            applySeventhDay: config.HasSeventhDayRule);
    }
}
