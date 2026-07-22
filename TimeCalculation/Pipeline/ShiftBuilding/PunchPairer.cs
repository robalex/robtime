using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.ShiftBuilding;

/// <summary>
/// Stage 2 — Pairing.
/// Matches In/Out clock punches into PunchPairs.
/// FixedDollar/FixedHours punches are separated out rather than paired.
///
/// Splitting: any pair whose time range spans a PayRule OR EmployeePosition effective-date
/// boundary is split at midnight of the boundary date (in the employee's timezone).  Each
/// resulting sub-pair carries the PayRule active during its interval, so later stages always
/// see a single rule per pair without needing to run the whole pipeline again.  Position/rate
/// is attached per sub-pair by Stage 4, which now sees a single position per pair for the
/// same reason.
///
/// Orphan handling:
///   • An In with no following Out, An Out with no preceding In, A pair exceeding MaxShiftLengthHours
///     → In treated as orphan, incomplete pairs created.
///
/// Invariant: pendingPair is always either null or holds an InPunch waiting for its Out — an
/// orphan Out (no preceding In available) is finalized into the result immediately rather than
/// held pending, so it can never be dereferenced as if it had an InPunch.
/// </summary>
public static class PunchPairer
{
    public static (IReadOnlyList<PunchPair> Pairs, IReadOnlyList<Punch> FixedEntries) PairPunches(
        IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var pairs = new List<PunchPair>();
        var fixedEntries = new List<Punch>();
        PunchPair? pendingPair = null;

        foreach (var punch in punches.OrderBy(p => p.EffectiveTime))
        {
            if (punch.IsFixedEntry)
            {
                fixedEntries.Add(punch);
                continue;
            }

            if (punch.Kind == PunchKind.In)
            {
                if (pendingPair is not null)
                {
                    pairs.Add(pendingPair);
                }

                pendingPair = new PunchPair { InPunch = punch };
                continue;
            }

            if (punch.Kind == PunchKind.Out)
            {
                if (pendingPair is null)
                {
                    pairs.Add(new PunchPair { OutPunch = punch });
                    continue;
                }

                var pendingIn = pendingPair.InPunch!;
                var rule = ctx.GetRuleAt(pendingIn.EffectiveTime);
                var hoursBetweenPunches = (decimal)(punch.EffectiveTime - pendingIn.EffectiveTime).TotalHours;

                if (hoursBetweenPunches > rule.MaxShiftLengthHours)
                {
                    pairs.Add(pendingPair);
                    pairs.Add(new PunchPair { OutPunch = punch });
                    pendingPair = null;
                }
                else
                {
                    pendingPair.OutPunch = punch;
                }
            }
        }

        if (pendingPair is not null)
        {
            pairs.Add(pendingPair);
        }

        return (SplitAtBoundaries(pairs, ctx), fixedEntries);
    }

    private static IReadOnlyList<PunchPair> SplitAtBoundaries(List<PunchPair> pairs, PipelineContext ctx)
    {
        var result = new List<PunchPair>(pairs.Count);

        foreach (var pair in pairs)
        {
            if (pair.IsMissingPunch)
            {
                var punch = pair.HasInPunch ? pair.InPunch : pair.OutPunch;
                result.Add(pair with { AppliedRule = ctx.GetRuleAt(punch!.EffectiveTime) });
                continue;
            }

            var boundaries = ctx.GetBoundaryInstantsBetween(
                pair.InPunch!.EffectiveTime, pair.OutPunch!.EffectiveTime);

            if (boundaries.Count == 0)
            {
                result.Add(pair with { AppliedRule = ctx.GetRuleAt(pair.InPunch.EffectiveTime) });
                continue;
            }

            var splitPoints = Enumerable.Empty<Instant>()
                .Append(pair.InPunch.EffectiveTime)
                .Concat(boundaries)
                .Append(pair.OutPunch.EffectiveTime)
                .ToList();

            for (int i = 0; i < splitPoints.Count - 1; i++)
            {
                var segStart = splitPoints[i];
                var segEnd = splitPoints[i + 1];

                // Preserve original punch objects for start/end; use synthetic records for interior
                var inPunch = i == 0 ? pair.InPunch
                    : pair.InPunch with { PunchTime = segStart, RoundedPunchTime = null, Id = 0 };
                var outPunch = i == splitPoints.Count - 2 ? pair.OutPunch
                    : pair.OutPunch with { PunchTime = segEnd, RoundedPunchTime = null, Id = 0 };

                result.Add(new PunchPair
                {
                    InPunch = inPunch,
                    OutPunch = outPunch,
                    AppliedRule = ctx.GetRuleAt(segStart),
                    IsSplit = true,
                });
            }
        }

        return result;
    }
}
