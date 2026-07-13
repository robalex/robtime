using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// Colorado rest premium (COMPS Order): a paid 10-minute rest per 4 hours worked.  A shortfall owes
/// one hour at the regular rate and cannot be waived.  Follows the same "one per 4 hours or major
/// fraction" schedule as California; verify exact thresholds against the current COMPS order.
/// </summary>
public class CoRestPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "CO_REST";
    public override string Code => RuleCode;
    public override Jurisdiction Jurisdiction => Jurisdiction.Colorado;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;

    public override bool Applies(ShiftAnalysis analysis, PremiumContext ctx) =>
        analysis.WorkedHours >= 3.5m;

    public override PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx)
    {
        int required = RestSchedule.Required(analysis.WorkedHours);
        int taken = analysis.RestBreakCount();
        bool violated = taken < required;

        return Resolve(ctx, violated, 1m, 1.0m,
            $"Rest periods provided ({taken}) fewer than required ({required}) (CO rest law).",
            "Rest period requirements satisfied.");
    }
}
