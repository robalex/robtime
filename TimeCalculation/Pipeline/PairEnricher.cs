using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 4 — Pair enrichment.
/// Attaches the effective Position and Rate to each PunchPair.
/// If the punch carries a PositionId override, that position is used.
/// Otherwise the employee's position assignment active at the In time is used.
/// Falls back to Employee.MinimumWage when no position is found.
/// </summary>
public static class PairEnricher
{
    public static IReadOnlyList<PunchPair> AttachPositionAndRateToPunchPairs(IReadOnlyList<PunchPair> pairs, PipelineContext ctx)
        => pairs.Select(p => AttachPositionAndRateToPunchPair(p, ctx)).ToList();

    private static PunchPair AttachPositionAndRateToPunchPair(PunchPair pair, PipelineContext ctx)
    {
        var position = ctx.GetPositionAt(pair.InPunch.EffectiveTime, pair.InPunch.PositionId);
        return pair with
        {
            Position = position,
            Rate = position?.BaseRate ?? ctx.Employee.MinimumWage,
        };
    }
}
