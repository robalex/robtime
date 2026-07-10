using TimeCalculation.Model;
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
    public override Jurisdiction Jurisdiction => Jurisdiction.California;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;

    public override bool Applies(Shift shift, PremiumContext ctx) =>
        ShiftAnalysis.From(shift).WorkedHours >= 3.5m;

    public override PremiumResult Calculate(Shift shift, PremiumContext ctx)
    {
        var a = ShiftAnalysis.From(shift);
        int required = RestSchedule.Required(a.WorkedHours);
        int taken = a.RestBreakCount();
        bool violated = taken < required;

        return Resolve(ctx, violated, 1m, ctx.RegularRate,
            $"Rest periods provided ({taken}) fewer than required ({required}) (CA rest law).",
            "Rest period requirements satisfied.");
    }
}
