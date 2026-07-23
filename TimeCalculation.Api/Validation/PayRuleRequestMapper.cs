using TimeCalculation.Api.Contracts;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Builds/updates a <see cref="PayRule"/> from a request, applying only the fields the request
/// actually supplied (see <see cref="PayRuleFieldsRequest"/>'s own doc comment — never duplicate
/// <see cref="PayRule"/>'s own defaults here). Pure, no DB access, so this and
/// <see cref="PayRuleRequestValidator"/> are unit-testable without a database.
/// </summary>
public static class PayRuleRequestMapper
{
    public static PayRule BuildFromRequest(CreatePayRuleRequest request)
    {
        var payRule = new PayRule { ClientId = request.ClientId, Name = request.Name };
        ApplySharedFields(payRule, request);
        return payRule;
    }

    /// <summary>Mutates an existing PayRule in place — omitted fields keep their current persisted
    /// value, they do not reset to PayRule's compile-time default (see UpdatePayRuleRequest's own
    /// doc comment for why that's a different merge semantic than Create's).</summary>
    public static void ApplyUpdate(PayRule payRule, UpdatePayRuleRequest request)
    {
        if (request.Name is { } name)
        {
            payRule.Name = name;
        }

        ApplySharedFields(payRule, request);
    }

    private static void ApplySharedFields(PayRule payRule, PayRuleFieldsRequest request)
    {
        if (request.Description is { } description)
        {
            payRule.Description = description;
        }

        if (request.TemplateCode is { } templateCode)
        {
            payRule.TemplateCode = templateCode;
        }

        if (request.TemplateVersion is { } templateVersion)
        {
            payRule.TemplateVersion = templateVersion;
        }

        if (request.PunchPairResetHours is { } resetHrs)
        {
            payRule.PunchPairResetHours = resetHrs;
        }

        if (request.MaxShiftLengthHours is { } maxShift)
        {
            payRule.MaxShiftLengthHours = maxShift;
        }

        if (request.DistanceBetweenShiftsHours is { } distance)
        {
            payRule.DistanceBetweenShiftsHours = distance;
        }

        if (request.ExpectedBreakLengthMinutes is { } breakLen)
        {
            payRule.ExpectedBreakLengthMinutes = breakLen;
        }

        if (request.ExpectedLunchLengthMinutes is { } lunchLen)
        {
            payRule.ExpectedLunchLengthMinutes = lunchLen;
        }

        if (request.ShiftDateStrategy is { } dateStrategy)
        {
            payRule.ShiftDateStrategy = dateStrategy;
        }

        if (request.WorkweekStartDay is { } weekStart)
        {
            payRule.WorkweekStartDay = weekStart;
        }

        if (request.ActivePremiumCodes is { } premiumCodes)
        {
            payRule.ActivePremiumCodes = premiumCodes;
        }

        if (request.ActiveDifferentialCodes is { } differentialCodes)
        {
            payRule.ActiveDifferentialCodes = differentialCodes;
        }

        if (request.RoundingStrategy is { } roundingStrategy)
        {
            payRule.RoundingRule.RoundingStrategy = roundingStrategy;
        }

        if (request.RoundingIntervalMinutes is { } roundingInterval)
        {
            payRule.RoundingRule.RoundingIntervalMinutes = roundingInterval;
        }

        if (request.RoundingGraceMinutes is { } roundingGrace)
        {
            payRule.RoundingRule.RoundingGraceMinutes = roundingGrace;
        }

        if (request.WeeklyOvertimeThresholdHours is { } weeklyOt)
        {
            payRule.OvertimeRule.WeeklyOvertimeThresholdHours = weeklyOt;
        }

        if (request.HasDailyOvertime is { } hasDaily)
        {
            payRule.OvertimeRule.HasDailyOvertime = hasDaily;
        }

        if (request.DailyOvertimeThresholdHours is { } dailyOt)
        {
            payRule.OvertimeRule.DailyOvertimeThresholdHours = dailyOt;
        }

        if (request.DailyDoubletimeThresholdHours is { } dailyDt)
        {
            payRule.OvertimeRule.DailyDoubletimeThresholdHours = dailyDt;
        }

        if (request.HasSeventhDayRule is { } hasSeventh)
        {
            payRule.OvertimeRule.HasSeventhDayRule = hasSeventh;
        }
    }
}
