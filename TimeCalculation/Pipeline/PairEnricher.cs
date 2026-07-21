using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 3 — Pair enrichment.
/// Attaches the effective Position and Rate to each PunchPair.
/// If the punch carries a PositionId override, that position is used.
/// Otherwise the employee's position assignment active at the In time is used.
/// Falls back to Employee.MinimumWage when no position is found.
///
/// An orphan pair (In with no Out, or Out with no In) has no rate that matters for pay — its
/// TotalHours is 0 and downstream stages skip it for earnings — but Position/Rate are still
/// resolved from whichever punch exists, so a later manual correction that completes the pair
/// doesn't need this stage to re-run.
/// </summary>
public static class PairEnricher
{
    public static IReadOnlyList<PunchPair> AttachPositionAndRateToPunchPairs(IReadOnlyList<PunchPair> pairs, PipelineContext ctx)
        => pairs.Select(p => AttachPositionAndRateToPunchPair(p, ctx)).ToList();

    private static PunchPair AttachPositionAndRateToPunchPair(PunchPair pair, PipelineContext ctx)
    {
        var anchor = pair.InPunch ?? pair.OutPunch;
        if (anchor is null) return pair;

        var position = ctx.GetPositionAt(anchor.EffectiveTime, anchor.PositionId);
        return pair with
        {
            Position = position,
            Rate = position?.BaseRate ?? ctx.Employee.MinimumWage,
        };
    }
}
