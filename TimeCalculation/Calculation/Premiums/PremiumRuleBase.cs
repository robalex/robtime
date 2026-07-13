using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>Shared waiver handling and result construction for premium rules.</summary>
public abstract class PremiumRuleBase : IPremiumRule
{
    public abstract string Code { get; }
    public abstract Jurisdiction Jurisdiction { get; }
    public abstract WaiverPolicy WaiverPolicy { get; }

    public abstract bool Applies(ShiftAnalysis analysis, PremiumContext ctx);
    public abstract PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx);

    /// <summary>Builds the result: no violation → zero; violation → premium unless waived by policy.</summary>
    protected PremiumResult Resolve(
        PremiumContext ctx, bool violated, decimal hours, decimal rate, string violatedMsg, string okMsg)
    {
        if (!violated)
            return new PremiumResult { Code = Code, Violated = false, WaiverPolicy = WaiverPolicy, Explanation = okMsg };

        bool waived = WaiverEvaluator.IsWaived(WaiverPolicy, ctx.Overrides);

        return new PremiumResult
        {
            Code = Code,
            Violated = true,
            Waived = waived,
            WaiverPolicy = WaiverPolicy,
            Hours = waived ? 0 : hours,
            Amount = waived ? 0 : hours * rate,
            Explanation = waived ? $"{violatedMsg} — waived by override." : violatedMsg,
        };
    }
}

/// <summary>Statutory rest schedule: one paid rest per 4 hours worked (or major fraction thereof).</summary>
internal static class RestSchedule
{
    public static int Required(decimal hours) =>
        hours < 3.5m ? 0 : hours <= 6m ? 1 : hours <= 10m ? 2 : hours <= 14m ? 3 : 4;
}
