using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape/consistency validation — no DB access, so this is unit-testable
/// on its own.</summary>
public static class PayRuleRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePayRuleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        return errors;
    }

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

    /// <summary>
    /// Whether a rule can still be edited or deleted in place. Only Draft — the whole point of Gap
    /// F's versioning design is that an Active or Superseded rule is never mutated (PayRuleAssignments
    /// and PayCalculationSnapshots may already reference it by its current (Id, Version)). The
    /// "create a new version instead of editing" workflow (<see cref="CanForkNewVersion"/>) is how
    /// you change an Active/Superseded rule instead.
    /// </summary>
    public static bool IsMutable(PayRule payRule) => payRule.Status == PayRuleStatus.Draft;

    /// <summary>Only a Draft can be promoted — Active/Superseded are already past this transition,
    /// and there's no path back from either.</summary>
    public static bool CanActivate(PayRule payRule) => payRule.Status == PayRuleStatus.Draft;

    /// <summary>Forking a new version only makes sense from a rule that's already been published —
    /// a Draft is edited directly (<see cref="IsMutable"/>), not forked from.</summary>
    public static bool CanForkNewVersion(PayRule payRule) => payRule.Status != PayRuleStatus.Draft;
}
