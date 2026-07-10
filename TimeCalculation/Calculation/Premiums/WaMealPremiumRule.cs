using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// Washington meal premium: for shifts over 5 hours a 30-minute meal is required (between the 2nd
/// and 5th hour).  When missed, the remedy is paying the employee for the 30 minutes that should
/// have been unpaid — modeled here as a HALF-hour premium at the regular rate.
///
/// Waiver policy is an open legal question (PLAN open decision #1) — defaulted to NotWaivable.
/// </summary>
public class WaMealPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "WA_MEAL";
    public override string Code => RuleCode;
    public override Jurisdiction Jurisdiction => Jurisdiction.Washington;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;   // TODO: verify WA waiver rules

    public override bool Applies(Shift shift, PremiumContext ctx) =>
        ShiftAnalysis.From(shift).WorkedHours > 5m;

    public override PremiumResult Calculate(Shift shift, PremiumContext ctx)
    {
        var a = ShiftAnalysis.From(shift);
        bool violated = !a.HasQualifyingMeal(30m, byWorkedHour: 5m);

        return Resolve(ctx, violated, 0.5m, ctx.RegularRate,
            "No compliant 30-minute meal period; 30 minutes paid in lieu (WA meal law).",
            "Meal period requirements satisfied.");
    }
}
