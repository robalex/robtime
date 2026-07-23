using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape/consistency validation — no DB access, so this is unit-testable
/// on its own.</summary>
public static class PayRuleRequestValidator
{
    /// <summary>
    /// A grace window larger than half the interval leaves no dead zone — every punch would round
    /// down to the bucket start and RoundWithGrace's forward-rounding branch would never fire (see
    /// PunchRounder.RoundWithGrace). Validates the fully-resolved <see cref="PayRule"/> (defaults
    /// already applied by <see cref="PayRuleRequestMapper"/>), not the raw request — a request that
    /// omits both fields can still produce an invalid combination against PayRule's own defaults.
    /// </summary>
    public static IDictionary<string, string[]> ValidateConsistency(PayRule payRule)
    {
        var errors = new Dictionary<string, string[]>();
        if (payRule.RoundingRule.RoundingStrategy == RoundingStrategy.IntervalWithGrace
            && payRule.RoundingRule.RoundingGraceMinutes > payRule.RoundingRule.RoundingIntervalMinutes / 2.0)
        {
            errors["roundingGraceMinutes"] =
                [$"Grace minutes ({payRule.RoundingRule.RoundingGraceMinutes}) must be at most half the rounding interval ({payRule.RoundingRule.RoundingIntervalMinutes})."];
        }

        return errors;
    }
}
