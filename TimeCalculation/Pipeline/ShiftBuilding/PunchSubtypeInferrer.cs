using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Pipeline.ShiftBuilding;

/// <summary>
/// Stage 5 — Punch-subtype inference.
/// Runs after shifts are built, so shift-boundary decisions are already settled: any Out→In gap
/// between two consecutive PunchPairs within the same Shift is by construction a mid-shift gap
/// (ShiftBuilder would have split it into a new Shift otherwise), and a Shift's first In / last Out
/// can never be a candidate. This stage only has to classify each such gap as a Break or Lunch by
/// comparing its duration to the PayRule's expected break/lunch lengths (nearest wins) — it no
/// longer needs its own DistanceBetweenShiftsHours check to tell mid-shift gaps from shift
/// boundaries.
///
/// A zero-length gap (OutPunch and next InPunch at the same instant) is a PunchPairer boundary-split
/// continuation, not a real gap, and is skipped.
///
/// A punch arriving with a non-null Subtype was forced by a supervisor or the employee: inference
/// never overwrites it, and the forced value propagates to the other punch of the gap when that one
/// is unresolved. All clock punches leave this stage with a resolved (non-null) Subtype;
/// FixedDollar/FixedHours punches (carried in Shift.FixedEntries, not PunchPairs) are untouched.
/// </summary>
public static class PunchSubtypeInferrer
{
    public static IReadOnlyList<Shift> InferPunchSubtypes(IReadOnlyList<Shift> shifts, PipelineContext ctx)
        => shifts.Select(s => InferForShift(s, ctx)).ToList();

    private static Shift InferForShift(Shift shift, PipelineContext ctx)
    {
        var pairs = shift.PunchPairs.OrderBy(AnchorTime).ToList();
        if (pairs.Any(p => p.IsMissingPunch ))
        {
            return shift;  // don't infer anything for shifts with missing punches, leave them as-is
        }

        for (int i = 0; i < pairs.Count - 1; i++)
        {
            var priorOut = pairs[i].OutPunch;
            var nextIn = pairs[i + 1].InPunch;

            var gap = nextIn!.EffectiveTime - priorOut!.EffectiveTime;
            if (gap <= Duration.Zero)
            {
                continue;   // boundary-split continuation, not a real gap
            }

            var rule = ctx.GetRuleAt(priorOut.EffectiveTime);
            var subtype = priorOut.Subtype ?? nextIn.Subtype ?? Classify(nextIn, priorOut, rule);

            if (priorOut.Subtype is null)
            {
                pairs[i] = pairs[i] with { OutPunch = priorOut with { Subtype = subtype } };
            }

            if (nextIn.Subtype is null)
            {
                pairs[i + 1] = pairs[i + 1] with { InPunch = nextIn with { Subtype = subtype } };
            }
        }

        // Any clock punch not part of a qualifying gap resolves to None.
        for (int i = 0; i < pairs.Count; i++)
        {
            pairs[i] = pairs[i] with
            {
                InPunch = pairs[i].InPunch is { Subtype: null } ip ? ip with { Subtype = PunchSubtype.None } : pairs[i].InPunch,
                OutPunch = pairs[i].OutPunch is { Subtype: null } op ? op with { Subtype = PunchSubtype.None } : pairs[i].OutPunch,
            };
        }

        return shift with { PunchPairs = pairs };
    }

    // A pair's own anchor time: its In, or its Out when it's an orphan Out with no In.
    private static Instant AnchorTime(PunchPair pair) => pair.InPunch?.EffectiveTime ?? pair.OutPunch!.EffectiveTime;

    private static PunchSubtype Classify(Punch inPunch, Punch outPunch, PayRule rule)
    {
        var gapMinutes = (decimal)(inPunch.EffectiveTime - outPunch.EffectiveTime).TotalMinutes;
        var distToBreak = Math.Abs(gapMinutes - rule.ExpectedBreakLengthMinutes);
        var distToLunch = Math.Abs(gapMinutes - rule.ExpectedLunchLengthMinutes);
        return distToBreak <= distToLunch ? PunchSubtype.Break : PunchSubtype.Lunch;
    }
}
