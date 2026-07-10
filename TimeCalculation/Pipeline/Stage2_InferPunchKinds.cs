using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 2 — Punch-kind inference.
/// Converts Clock punches to In or Out by examining the sequence of prior punches.
/// FixedDollar/FixedHours punches and already-resolved In/Out punches pass through unchanged.
///
/// Reset rule: if the gap since the last In exceeds PayRule.PunchPairResetHours, the prior
/// In is treated as an orphan and the new punch starts a fresh In.
/// </summary>
public static class Stage2_InferPunchKinds
{
    //TODO: I think that we actually should accept in/out punches and should infer break/lunch instead of inferring in/out. We need to
    //      figure out what to do if you clocked in > 15hrs ago and then try to add a punch. Do we show "clock in" or "clock out?" Do we let you force it?
    public static IReadOnlyList<Punch> Execute(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var result = new List<Punch>(punches.Count);
        Punch? lastClockPunch = null;

        foreach (var punch in punches.OrderBy(p => p.EffectiveTime))
        {
            if (punch.IsFixedEntry)
            {
                result.Add(punch);
                continue;
            }

            if (punch.Kind is PunchKind.In or PunchKind.Out)
            {
                // Already resolved by supervisor; track it but don't modify
                result.Add(punch);
                lastClockPunch = punch;
                continue;
            }

            // PunchKind.Clock — infer
            var rule = ctx.GetRuleAt(punch.EffectiveTime);
            var inferred = Infer(punch, lastClockPunch, rule);
            var resolved = punch with { Kind = inferred };
            result.Add(resolved);
            lastClockPunch = resolved;
        }

        return result;
    }

    private static PunchKind Infer(Punch punch, Punch? last, PayRule rule)
    {
        if (last is null) return PunchKind.In;
        if (last.Kind == PunchKind.Out) return PunchKind.In;

        // last.Kind == In — check reset window
        var gapHours = (decimal)(punch.EffectiveTime - last.EffectiveTime).TotalHours;
        return gapHours > rule.PunchPairResetHours ? PunchKind.In : PunchKind.Out;
    }
}
