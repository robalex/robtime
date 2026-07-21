using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// Stage 8 — Time-based differentials.
/// For each shift and each active DifferentialRule, intersects the shift's worked time with the
/// rule's day schedule and time-of-day window, apportioning qualifying hours.  If the qualifying
/// hours meet the rule's minimum, an AppliedDifferential is attached to the shift.
///
/// The window is interpreted differently by day mode:
///   • ConsecutiveDayRange — WindowStart/WindowEnd bound ONE continuous span per range occurrence,
///     from WindowStart on the range's first day to WindowEnd on its last day (e.g. noon Thursday
///     to 5pm Monday, with the interior days fully covered).
///   • every other mode — WindowStart/WindowEnd is a per-day window applied independently on each
///     active day (e.g. noon–5pm on each listed weekday).
///
/// Amount:
///   FlatPerHour → qualifyingHours × AdjustmentValue
///   Multiplier  → Σ (segmentHours × pairRate × AdjustmentValue)   (rate-weighted per pair)
///   FixedBonus  → AdjustmentValue once, when the shift qualifies
///
/// Stacking: qualifying differentials stack additively by default.  Rules that share a non-empty
/// DifferentialRule.ExclusivityGroup are mutually exclusive — only the highest-amount one in the
/// group applies to the shift (ties broken by Code for determinism).
///
/// A shift with any missing punch (Shift.HasMissingPunches) is skipped entirely — an orphan pair
/// has no real worked interval to intersect against a rule's time-of-day window.
/// </summary>
public static class DifferentialApplier
{
    public static IReadOnlyList<Shift> ApplyDifferentials(IReadOnlyList<Shift> shifts, PipelineContext ctx)
    {
        if (ctx.DifferentialRules.Count == 0)
        {
            return shifts;
        }

        return shifts.Select(s => Apply(s, ctx)).ToList();
    }

    private static Shift Apply(Shift shift, PipelineContext ctx)
    {
        if (shift.HasMissingPunches)
        {
            return shift;
        }

        var candidates = new List<DifferentialCandidate>();

        foreach (var rule in ctx.DifferentialRules)
        {
            decimal qualifyingHours = 0;
            decimal perHourAmount = 0;

            foreach (var pair in shift.PunchPairs)
            {
                var rate = pair.Rate ?? 0;
                var hrs = CalculateQualifyingHoursForPair(rule, pair, ctx);
                if (hrs <= 0) continue;

                qualifyingHours += hrs;
                if (rule.AdjustmentType == DifferentialAdjustmentType.FlatPerHour)
                {
                    perHourAmount += hrs * rule.AdjustmentValue;
                }
                else if (rule.AdjustmentType == DifferentialAdjustmentType.Multiplier)
                {
                    perHourAmount += hrs * rate * rule.AdjustmentValue;
                }
            }

            if (qualifyingHours < rule.MinHoursInWindow || qualifyingHours <= 0)
                continue;

            var amount = rule.AdjustmentType == DifferentialAdjustmentType.FixedBonus
                ? rule.AdjustmentValue
                : perHourAmount;

            candidates.Add((new DifferentialCandidate(rule, new AppliedDifferential
            {
                Code = rule.Code,
                Hours = qualifyingHours,
                Amount = amount,
                AdjustmentType = rule.AdjustmentType,
                AdjustmentValue = rule.AdjustmentValue,
            })));
        }

        var applied = ResolveExclusivity(candidates);
        return applied.Count == 0 ? shift : shift with { Differentials = applied };
    }

    // Ungrouped differentials all apply; within each exclusivity group only the highest-amount
    // one survives (ties broken by Code). Original evaluation order is preserved.
    private static List<AppliedDifferential> ResolveExclusivity(
        List<DifferentialCandidate> candidates)
    {
        var winningGroupCodes = candidates
            .Where(c => !string.IsNullOrEmpty(c.Rule.ExclusivityGroup))
            .GroupBy(c => c.Rule.ExclusivityGroup)
            .Select(g => g
                .OrderByDescending(c => c.Applied.Amount)
                .ThenBy(c => c.Rule.Code, StringComparer.Ordinal)
                .First().Rule.Code)
            .ToHashSet();

        var result = new List<AppliedDifferential>();
        foreach (var (rule, applied) in candidates)
        {
            bool grouped = !string.IsNullOrEmpty(rule.ExclusivityGroup);
            if (!grouped || winningGroupCodes.Contains(rule.Code))
                result.Add(applied);
        }
        return result;
    }

    // Qualifying hours a single pair contributes to a rule. ConsecutiveDayRange treats the window
    // as one continuous multi-day span (see ContinuousRangeQualifyingHours); every other mode
    // applies the window per active day.
    private static decimal CalculateQualifyingHoursForPair(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
        => rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange
            ? ContinuousRangeQualifyingHoursCalculator.Calculate(rule, pair, ctx)
            : PerDayQualifyingHoursCalculator.Calculate(rule, pair, ctx);
}
