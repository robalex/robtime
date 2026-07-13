using System.Runtime.InteropServices;
using NodaTime;
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

        // Shifts from BuildFromPairs are already in ascending time order (built by walking pairs
        // sorted by InPunch time), so their (start, end) ranges are sorted ascending and
        // non-overlapping — binary search applies directly instead of scanning every shift.
        var ranges = result.Select(ShiftRange).ToList();

        foreach (var entry in fixedEntries.OrderBy(e => e.EffectiveTime))
        {
            var nearestIdx = FindNearestShiftIndex(ranges, entry.EffectiveTime);
            var nearest = result[nearestIdx];
            result[nearestIdx] = nearest with { FixedEntries = [.. nearest.FixedEntries, entry] };
        }

        return result;
    }

    // A shift's overall time span: its first pair's In and its last pair's Out (falling back to
    // the other punch of that pair when one is missing). Shifts built by BuildFromPairs always
    // have at least one PunchPair.
    private static (Instant Start, Instant End) ShiftRange(Shift shift)
    {
        var first = shift.PunchPairs[0];
        var last = shift.PunchPairs[^1];
        var start = first.InPunch?.EffectiveTime ?? first.OutPunch!.EffectiveTime;
        var end = last.OutPunch?.EffectiveTime ?? last.InPunch!.EffectiveTime;
        return (start, end);
    }

    // Finds the shift whose range contains `time`, or — if `time` falls in the gap between two
    // shifts — whichever neighbor's boundary is nearer (ties go to the earlier shift, matching the
    // original brute-force scan's strict "<" replacement rule). Any point strictly inside a shift's
    // range is necessarily closer to that shift than to any other shift's punches, since shifts are
    // non-overlapping and separated by the gap that caused ShiftBuilder to split them — so this
    // reduces to the same winning index as scanning every individual punch would, in O(log S).
    //
    // Uses the framework's MemoryExtensions.BinarySearch (by a custom IComparable key) instead of
    // a hand-rolled lo/hi/mid loop. Unlike PipelineContext's equivalent search, no duplicate-key
    // tie-break is needed here: two shifts can never share the same Start, because ShiftBuilder
    // only starts a new shift after a gap strictly greater than zero from the previous one.
    private static int FindNearestShiftIndex(List<(Instant Start, Instant End)> ranges, Instant time)
    {
        int found = CollectionsMarshal.AsSpan(ranges).BinarySearch(new ShiftStartKey(time));
        if (found >= 0)
            return found;   // time exactly matches a shift's Start — trivially inside that shift

        int before = ~found - 1;   // ~found = index of the first shift with Start > time
        int after = ~found;

        if (before < 0)
            return after;
        if (time <= ranges[before].End)
            return before;
        if (after >= ranges.Count)
            return before;

        var beforeDist = Math.Abs((ranges[before].End - time).TotalSeconds);
        var afterDist = Math.Abs((ranges[after].Start - time).TotalSeconds);
        return beforeDist <= afterDist ? before : after;
    }

    private readonly struct ShiftStartKey(Instant time) : IComparable<(Instant Start, Instant End)>
    {
        public int CompareTo((Instant Start, Instant End) other) => time.CompareTo(other.Start);
    }
}
