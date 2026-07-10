using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>
/// Puerto Rico meal premium: an employee working more than 5 hours must take a meal period between
/// the 3rd and 6th hour.  A violation owes one hour at the OVERTIME rate (distinct from the other
/// states, which use the regular rate).
///
/// Waiver policy is an open legal question (PLAN open decision #1) — defaulted to NotWaivable until
/// confirmed.
/// </summary>
public class PrMealPremiumRule : PremiumRuleBase
{
    public const string RuleCode = "PR_MEAL";
    public override string Code => RuleCode;
    public override Jurisdiction Jurisdiction => Jurisdiction.PuertoRico;
    public override WaiverPolicy WaiverPolicy => WaiverPolicy.NotWaivable;   // TODO: verify PR waiver rules

    public override bool Applies(Shift shift, PremiumContext ctx) =>
        ShiftAnalysis.From(shift).WorkedHours > 5m;

    public override PremiumResult Calculate(Shift shift, PremiumContext ctx)
    {
        var a = ShiftAnalysis.From(shift);
        bool violated = !a.HasQualifyingMeal(30m, byWorkedHour: 6m);

        return Resolve(ctx, violated, 1m, ctx.OvertimeRate,
            "No meal period taken between the 3rd and 6th hour (PR meal law).",
            "Meal period requirements satisfied.");
    }
}
