using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.ShiftBuilding;

/// <summary>
/// Splits a Shift that crosses local midnight into one Shift per calendar day it touches, each
/// dated to its own day. Backs ShiftDateStrategy.SplitAtMidnight (Stage 6).
///
/// Any PunchPair straddling midnight is itself split at the boundary, mirroring
/// PunchPairer.SplitAtBoundaries: the original punch objects are kept at the outer edges and the
/// interior boundary punches are synthetic copies (Id = 0, rounding cleared) carrying the boundary
/// instant. Position/Rate/AppliedRule ride along on each piece, so downstream stages still see a
/// fully-enriched pair.
///
/// Consequence worth knowing: because each piece is a separate Shift on its own date, per-day
/// concerns downstream (daily overtime, per-shift premiums, per-shift pay line items) see two
/// shifts rather than one. That is the point of the strategy — hours land on the day worked.
/// </summary>
internal static class MidnightShiftSplitter
{
    public static IEnumerable<Shift> Split(Shift shift, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;

        // A shift with no pairs (e.g. a standalone fixed-entry shift) has nothing to split.
        if (shift.PunchPairs.Count == 0)
        {
            yield return shift;
            yield break;
        }

        var pairsByDate = new SortedDictionary<LocalDate, List<PunchPair>>();
        foreach (var pair in shift.PunchPairs)
        {
            foreach (var piece in SplitPairAtMidnights(pair, zone))
            {
                var date = AnchorTime(piece).InZone(zone).Date;
                if (!pairsByDate.TryGetValue(date, out var forDate))
                {
                    pairsByDate[date] = forDate = [];
                }
                forDate.Add(piece);
            }
        }

        var dates = pairsByDate.Keys.ToList();
        var entriesByDate = DistributeFixedEntries(shift.FixedEntries, dates, zone);

        foreach (var date in dates)
        {
            yield return shift with
            {
                ShiftDate = date,
                PunchPairs = pairsByDate[date],
                FixedEntries = entriesByDate.TryGetValue(date, out var entries) ? entries : [],
            };
        }
    }

    // Each fixed entry goes to the resulting shift for its own local date, falling back to the
    // earliest one when it lands on a day none of the split pieces cover.
    private static Dictionary<LocalDate, List<Punch>> DistributeFixedEntries(
        IReadOnlyList<Punch> fixedEntries, List<LocalDate> dates, DateTimeZone zone)
    {
        var byDate = new Dictionary<LocalDate, List<Punch>>();
        foreach (var entry in fixedEntries)
        {
            var entryDate = entry.EffectiveTime.InZone(zone).Date;
            var target = dates.Contains(entryDate) ? entryDate : dates[0];
            if (!byDate.TryGetValue(target, out var forDate))
            {
                byDate[target] = forDate = [];
            }
            forDate.Add(entry);
        }
        return byDate;
    }

    private static IEnumerable<PunchPair> SplitPairAtMidnights(PunchPair pair, DateTimeZone zone)
    {
        // An orphan pair has no interval to split — it belongs to whichever day its one punch is on.
        if (pair.IsMissingPunch)
        {
            yield return pair;
            yield break;
        }

        var inTime = pair.InPunch!.EffectiveTime;
        var outTime = pair.OutPunch!.EffectiveTime;

        var midnights = LocalMidnightsBetween(inTime, outTime, zone).ToList();
        if (midnights.Count == 0)
        {
            yield return pair;
            yield break;
        }

        var splitPoints = new List<Instant> { inTime };
        splitPoints.AddRange(midnights);
        splitPoints.Add(outTime);

        for (int i = 0; i < splitPoints.Count - 1; i++)
        {
            var segStart = splitPoints[i];
            var segEnd = splitPoints[i + 1];

            // Preserve the original punch objects at the outer edges; interior boundary punches are
            // synthetic (Id = 0) so they never collide with real punch identities.
            var inPunch = i == 0
                ? pair.InPunch
                : pair.InPunch with { PunchTime = segStart, RoundedPunchTime = null, Id = 0 };
            var outPunch = i == splitPoints.Count - 2
                ? pair.OutPunch
                : pair.OutPunch with { PunchTime = segEnd, RoundedPunchTime = null, Id = 0 };

            yield return pair with { InPunch = inPunch, OutPunch = outPunch, IsSplit = true };
        }
    }

    // Local midnights strictly between start and end, in order.
    private static IEnumerable<Instant> LocalMidnightsBetween(Instant start, Instant end, DateTimeZone zone)
    {
        var date = start.InZone(zone).Date;
        while (true)
        {
            date = date.PlusDays(1);
            var midnight = date.AtMidnight().InZoneLeniently(zone).ToInstant();
            if (midnight >= end) yield break;
            yield return midnight;
        }
    }

    // A pair's own anchor time: its In, or its Out when it's an orphan Out with no In.
    private static Instant AnchorTime(PunchPair pair) => pair.InPunch?.EffectiveTime ?? pair.OutPunch!.EffectiveTime;
}
