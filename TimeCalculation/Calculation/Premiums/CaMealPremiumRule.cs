using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// California meal premium: a 30-minute meal must begin by the end of the 5th hour (and a second
/// by the end of the 10th hour for shifts over 10 hours).  A violation owes one hour at the
/// regular rate, capped at one premium per day regardless of how many meals were missed.
/// Waivable only with both a supervisor approval and an employee waiver.
/// </summary>
public class CaMealPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "CA_MEAL";
    public override string Code => RuleCode;
    public override Jurisdiction Jurisdiction => Jurisdiction.California;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.BothRequired;

    public override bool Applies(ShiftAnalysis analysis, PremiumContext ctx) =>
        analysis.WorkedHours > 5m;

    public override PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx)
    {
        int required = analysis.WorkedHours > 10m ? 2 : analysis.WorkedHours > 5m ? 1 : 0;

        bool firstMealOk = analysis.HasQualifyingMeal(30m, byWorkedHour: 5m);
        // Second meal must itself begin by the end of the 10th hour — a late second meal is a violation.
        bool secondMealOk = analysis.QualifyingMealCount(30m, byWorkedHour: 10m) >= 2;

        bool violated = (required >= 1 && !firstMealOk) || (required >= 2 && !secondMealOk);

        return Resolve(ctx, violated, 1m, ctx.RegularRate,
            "No compliant 30-minute meal period (CA meal law).",
            "Meal period requirements satisfied.");
    }
}
