using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// California rest premium: a paid 10-minute rest per 4 hours worked (or major fraction).  A
/// shortfall owes one hour at the regular rate, capped at one premium per day.  Not waivable.
/// Rest evidence is taken from clocked Break gaps (see ShiftAnalysis note).
/// </summary>
public class CaRestPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "CA_REST";
    public override string Code => RuleCode;
    public override string Name => "California Rest Premium";
    public override string Description =>
        "A paid 10-minute rest is required per 4 hours worked (or major fraction). A shortfall owes " +
        "one hour at the regular rate, capped at one premium per day. Not waivable.";
    public override Jurisdiction Jurisdiction => Jurisdiction.California;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;

    public override bool Applies(ShiftAnalysis analysis, PremiumContext ctx) =>
        analysis.WorkedHours >= 3.5m;

    public override PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx)
    {
        int required = RestSchedule.Required(analysis.WorkedHours);
        int taken = analysis.RestBreakCount();
        bool violated = taken < required;

        return Resolve(ctx, violated, 1m, 1.0m,
            $"Rest periods provided ({taken}) fewer than required ({required}) (CA rest law).",
            "Rest period requirements satisfied.");
    }
}
