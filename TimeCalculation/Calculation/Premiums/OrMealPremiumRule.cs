using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// Oregon meal premium: a 30-minute meal is required for shifts of 6 or more hours.  A missed or
/// short meal owes one hour at the regular rate.
///
/// Waiver policy is an open legal question (PLAN open decision #1) — defaulted to NotWaivable.
/// </summary>
public class OrMealPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "OR_MEAL";
    public override string Code => RuleCode;
    public override Jurisdiction Jurisdiction => Jurisdiction.Oregon;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;   // TODO: verify OR waiver rules

    public override bool Applies(ShiftAnalysis analysis, PremiumContext ctx) =>
        analysis.WorkedHours >= 6m;

    public override PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx)
    {
        bool violated = !analysis.HasQualifyingMeal(30m, byWorkedHour: 5m);

        return Resolve(ctx, violated, 1m, 1.0m,
            "No compliant 30-minute meal period (OR meal law).",
            "Meal period requirements satisfied.");
    }
}
