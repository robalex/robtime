namespace TimeCalculation.Pipeline.Differentials;

/// <summary>One candidate's exclusivity outcome: Won (its AppliedDifferential survives), or lost to
/// another rule in the same DifferentialRule.ExclusivityGroup (SupersededByCode names the winner).
/// Ungrouped candidates always win — exclusivity only ever eliminates candidates that share a
/// non-empty group.</summary>
internal readonly record struct ExclusivityOutcome(DifferentialCandidate Candidate, bool Won, string? SupersededByCode);

/// <summary>
/// Extracted from DifferentialApplier so both it (which only needs the winners) and the sandbox
/// explainer (which needs to say *why* a losing candidate lost) share one resolution — two readers
/// of the same decision instead of two implementations that could drift.
/// </summary>
internal static class ExclusivityResolver
{
    // Ungrouped differentials all win; within each exclusivity group only the highest-amount one wins
    // (ties broken by Code for determinism). Original evaluation order is preserved.
    internal static List<ExclusivityOutcome> Resolve(List<DifferentialCandidate> candidates)
    {
        var winnerByGroup = candidates
            .Where(c => !string.IsNullOrEmpty(c.Rule.ExclusivityGroup))
            .GroupBy(c => c.Rule.ExclusivityGroup)
            .ToDictionary(
                g => g.Key!,
                g => g.OrderByDescending(c => c.Applied.Amount).ThenBy(c => c.Rule.Code, StringComparer.Ordinal).First());

        var outcomes = new List<ExclusivityOutcome>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate.Rule.ExclusivityGroup))
            {
                outcomes.Add(new ExclusivityOutcome(candidate, Won: true, SupersededByCode: null));
                continue;
            }

            var winner = winnerByGroup[candidate.Rule.ExclusivityGroup];
            var won = winner.Rule.Code == candidate.Rule.Code;
            outcomes.Add(new ExclusivityOutcome(candidate, won, won ? null : winner.Rule.Code));
        }
        return outcomes;
    }
}
