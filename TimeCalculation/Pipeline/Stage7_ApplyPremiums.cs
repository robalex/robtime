using TimeCalculation.Calculation.Premiums;
using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 7 — Premiums.
/// Runs each shift through the premium rules named by the PayRule active on the shift date, honoring
/// per-shift overrides, and attaches the resulting PremiumResults (including waived/zero results, for
/// the audit trail) to the shift.
///
/// Premiums are "one hour at the regular rate", so the regular rate must be supplied per shift — it is
/// computed in Stage 11 from earnings that EXCLUDE premiums, so there is no circular dependency; the
/// orchestrator computes the regular rate first and passes it in via <paramref name="rateForShift"/>.
/// </summary>
public static class Stage7_ApplyPremiums
{
    public static IReadOnlyList<Shift> Execute(
        IReadOnlyList<Shift> shifts,
        PipelineContext ctx,
        Func<Shift, decimal> rateForShift,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift = null)
    {
        return shifts.Select(shift =>
        {
            var rule = ctx.GetRuleAt(FirstInstant(shift));
            if (rule.ActivePremiumCodes.Count == 0)
                return shift;

            var rules = PremiumRegistry.Resolve(rule.ActivePremiumCodes);
            var premiumCtx = new PremiumContext
            {
                RegularRate = rateForShift(shift),
                Overrides = overridesForShift?.Invoke(shift) ?? [],
            };

            var results = rules
                .Where(r => r.Applies(shift, premiumCtx))
                .Select(r => r.Calculate(shift, premiumCtx))
                .ToList();

            return results.Count == 0 ? shift : shift with { Premiums = results };
        }).ToList();
    }

    private static NodaTime.Instant FirstInstant(Shift shift) =>
        shift.PunchPairs
            .Where(p => p.HasInPunch)
            .Select(p => p.InPunch!.EffectiveTime)
            .DefaultIfEmpty(shift.ShiftDate.AtMidnight().InUtc().ToInstant())
            .Min();
}
