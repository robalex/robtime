using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 5 — Shift building.
/// Groups consecutive PunchPairs into Shifts.  A new Shift starts when the gap between
/// the last Out of the current shift and the In of the next pair exceeds
/// PayRule.DistanceBetweenShiftsHours (default 6 h).
///
/// Gap-related decisions always use the rule active at the gap's START (the prior Out), matching
/// PunchSubtypeInferrer's rule resolution for the same Out→In gap. This keeps the two components
/// from disagreeing at a rule-change boundary — e.g. one calling a gap a Lunch while the other
/// calls it a shift boundary, which would leave a Lunch-subtyped punch starting a "new" shift.
///
/// FixedDollar/FixedHours entries are attached to the nearest shift by punch time.
/// If no shifts exist, they are returned as standalone single-entry shifts.
/// </summary>
public static class ShiftBuilder
{
    public static IReadOnlyList<Shift> BuildShifts(
        IReadOnlyList<PunchPair> pairs,
        IReadOnlyList<Punch> fixedEntries,
        PipelineContext ctx)
    {
        var shifts = BuildFromPairs(pairs, ctx);
        return AttachFixedEntries(shifts, fixedEntries);
    }

    private static List<Shift> BuildFromPairs(IReadOnlyList<PunchPair> pairs, PipelineContext ctx)
    {
        var shifts = new List<Shift>();
        var currentPairs = new List<PunchPair>();

        foreach (var pair in pairs.OrderBy(p => p.InPunch.EffectiveTime))
        {
            if (currentPairs.Count == 0)
            {
                currentPairs.Add(pair);
                continue;
            }

            var lastOut = currentPairs.Last().OutPunch;
            var rule = ctx.GetRuleAt(lastOut?.EffectiveTime ?? pair.InPunch.EffectiveTime);

            var gapHours = lastOut is null ? decimal.MaxValue
                : (decimal)(pair.InPunch.EffectiveTime - lastOut.EffectiveTime).TotalHours;

            if (gapHours > rule.DistanceBetweenShiftsHours)
            {
                shifts.Add(new Shift { PunchPairs = currentPairs.ToList() });
                currentPairs = [pair];
            }
            else
            {
                currentPairs.Add(pair);
            }
        }

        if (currentPairs.Count > 0)
            shifts.Add(new Shift { PunchPairs = currentPairs.ToList() });

        return shifts;
    }

    private static IReadOnlyList<Shift> AttachFixedEntries(List<Shift> shifts, IReadOnlyList<Punch> fixedEntries)
    {
        if (fixedEntries.Count == 0) return shifts;

        if (shifts.Count == 0)
        {
            // No clock shifts — wrap each fixed entry in its own single-entry shift
            return fixedEntries
                .Select(e => new Shift { FixedEntries = [e] })
                .ToList();
        }

        var result = shifts.ToList();

        foreach (var entry in fixedEntries.OrderBy(e => e.EffectiveTime))
        {
            var nearestIdx = FindNearestShiftIndex(result, entry.EffectiveTime);
            var nearest = result[nearestIdx];
            result[nearestIdx] = nearest with { FixedEntries = [.. nearest.FixedEntries, entry] };
        }

        return result;
    }

    private static int FindNearestShiftIndex(List<Shift> shifts, NodaTime.Instant time)
    {
        int bestIdx = 0;
        double bestGap = double.MaxValue;

        for (int i = 0; i < shifts.Count; i++)
        {
            var gap = shifts[i].PunchPairs
                .SelectMany(p => new[] { p.InPunch?.EffectiveTime, p.OutPunch?.EffectiveTime })
                .OfType<NodaTime.Instant?>()
                .Select(t => Math.Abs((t!.Value - time).TotalSeconds))
                .DefaultIfEmpty(double.MaxValue)
                .Min();

            if (gap < bestGap) { bestGap = gap; bestIdx = i; }
        }

        return bestIdx;
    }
}
