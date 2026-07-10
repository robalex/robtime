using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 2 — Punch-subtype inference.
/// Employees punch In/Out explicitly; this stage classifies each mid-shift
/// Out→In gap as a Break or Lunch by comparing the gap duration to the
/// PayRule's expected break/lunch lengths (nearest wins).  The subtype is
/// stamped on both punches bounding the gap.
///
/// A gap only qualifies when it is within PayRule.DistanceBetweenShiftsHours —
/// anything longer is a shift boundary, so the first In and last Out of a
/// shift can never be a break or lunch.
///
/// A punch arriving with a non-null Subtype was forced by a supervisor or the
/// employee: inference never overwrites it, and the forced value propagates to
/// the other punch of the gap when that one is unresolved.  All clock punches
/// leave this stage with a resolved (non-null) Subtype; FixedDollar/FixedHours
/// punches pass through untouched.
/// </summary>
public static class Stage2_InferPunchSubtypes
{
    public static IReadOnlyList<Punch> Execute(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var result = new List<Punch>(punches.Count);
        int lastClockIndex = -1;

        foreach (var punch in punches.OrderBy(p => p.EffectiveTime))
        {
            if (punch.IsFixedEntry)
            {
                result.Add(punch);
                continue;
            }

            var current = punch;

            // A mid-shift gap is an Out followed by an In within the same shift
            if (current.Kind == PunchKind.In
                && lastClockIndex >= 0
                && result[lastClockIndex].Kind == PunchKind.Out)
            {
                var priorOut = result[lastClockIndex];
                var rule = ctx.GetRuleAt(priorOut.EffectiveTime);
                var gapHours = (decimal)(current.EffectiveTime - priorOut.EffectiveTime).TotalHours;

                if (gapHours <= rule.DistanceBetweenShiftsHours)
                {
                    var subtype = priorOut.Subtype
                        ?? current.Subtype
                        ?? Classify(current, priorOut, rule);

                    if (priorOut.Subtype is null)
                        result[lastClockIndex] = priorOut with { Subtype = subtype };
                    if (current.Subtype is null)
                        current = current with { Subtype = subtype };
                }
            }

            result.Add(current);
            lastClockIndex = result.Count - 1;
        }

        // Any clock punch not part of a qualifying gap resolves to None
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].IsClockPunch && result[i].Subtype is null)
                result[i] = result[i] with { Subtype = PunchSubtype.None };
        }

        return result;
    }

    private static PunchSubtype Classify(Punch inPunch, Punch outPunch, PayRule rule)
    {
        var gapMinutes = (decimal)(inPunch.EffectiveTime - outPunch.EffectiveTime).TotalMinutes;
        var distToBreak = Math.Abs(gapMinutes - rule.ExpectedBreakLengthMinutes);
        var distToLunch = Math.Abs(gapMinutes - rule.ExpectedLunchLengthMinutes);
        return distToBreak <= distToLunch ? PunchSubtype.Break : PunchSubtype.Lunch;
    }
}
