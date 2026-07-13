using NodaTime;
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
///
/// Per-workday cap: the statutory premium is owed once per workday per category, but a workday can
/// hold more than one shift (split shifts). After computing per-shift results, a second pass keeps at
/// most one PAID premium per (ShiftDate, code); later duplicates that day are demoted to a zero-amount
/// result that still records the violation for audit.
/// </summary>
public static class PremiumApplier
{
    public static IReadOnlyList<Shift> Execute(
        IReadOnlyList<Shift> shifts,
        PipelineContext ctx,
        Func<Shift, decimal> rateForShift,
        Func<Shift, IReadOnlyList<OverrideKind>>? overridesForShift = null)
    {
        var applied = shifts.Select(shift =>
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

            // Computed once per shift rather than per rule per method (Applies + Calculate), since
            // every rule today only needs this derived view, not the raw Shift.
            var analysis = ShiftAnalysis.From(shift);
            var anchorId = AnchorPunchId(shift);
            var results = rules
                .Where(r => r.Applies(analysis, premiumCtx))
                .Select(r => r.Calculate(analysis, premiumCtx) with
                {
                    AnchorPunchId = anchorId,
                    ShiftDate = shift.ShiftDate,
                })
                .ToList();

            return results.Count == 0 ? shift : shift with { Premiums = results };
        }).ToList();

        return CapPerWorkday(applied);
    }

    // Keeps at most one paid premium per (ShiftDate, code); the first paid one (in shift order) wins,
    // later ones that day are zeroed but retained (Violated = true) so the audit trail is complete.
    private static IReadOnlyList<Shift> CapPerWorkday(IReadOnlyList<Shift> shifts)
    {
        var paidSeen = new HashSet<(LocalDate, string)>();

        return shifts.Select(shift =>
        {
            if (shift.Premiums.Count == 0) return shift;

            bool changed = false;
            var capped = shift.Premiums.Select(p =>
            {
                if (p.IsPaid && !paidSeen.Add((shift.ShiftDate, p.Code)))
                {
                    changed = true;
                    return p with
                    {
                        Hours = 0m,
                        Amount = 0m,
                        Explanation = $"{p.Explanation} Capped: one {p.Code} premium per workday.",
                    };
                }
                return p;
            }).ToList();

            return changed ? shift with { Premiums = capped } : shift;
        }).ToList();
    }

    private static NodaTime.Instant FirstInstant(Shift shift) =>
        shift.PunchPairs
            .Where(p => p.HasInPunch)
            .Select(p => p.InPunch!.EffectiveTime)
            .DefaultIfEmpty(shift.ShiftDate.AtMidnight().InUtc().ToInstant())
            .Min();

    // The Id of the shift's earliest REAL In punch — anchors a stable (AnchorPunchId, Code) premium
    // identity. Boundary-split pairs (PunchPairer.SplitAtBoundaries) create synthetic interior
    // punches with Id = 0; if the earliest In happened to be one of those, every such shift would
    // anchor to the same 0 and collide. Prefer the earliest In with a real (non-zero) id, falling
    // back to 0 only when no real punch exists (e.g. a shift built entirely of synthetic pairs).
    private static int AnchorPunchId(Shift shift)
    {
        var inPunchesByTime = shift.PunchPairs
            .Where(p => p.HasInPunch)
            .OrderBy(p => p.InPunch!.EffectiveTime)
            .Select(p => p.InPunch!)
            .ToList();

        return inPunchesByTime.FirstOrDefault(p => p.Id != 0)?.Id
            ?? inPunchesByTime.FirstOrDefault()?.Id
            ?? 0;
    }
}
