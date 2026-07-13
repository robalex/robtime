using NodaTime;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Model;

public record Shift
{
    public IReadOnlyList<PunchPair> PunchPairs { get; init; } = [];
    public IReadOnlyList<Punch> FixedEntries { get; init; } = [];
    public IReadOnlyList<AppliedDifferential> Differentials { get; init; } = [];
    public IReadOnlyList<PremiumResult> Premiums { get; init; } = [];
    public LocalDate ShiftDate { get; init; }

    public decimal TotalHours => PunchPairs.Sum(p => p.TotalHours);

    /// <summary>
    /// Stable per-shift identity: the Id of the earliest REAL (non-synthetic) In punch. Used to
    /// anchor premium overrides and per-shift pay line items across recomputation.
    ///
    /// Boundary-split pairs (PunchPairer.SplitAtBoundaries) create synthetic interior punches with
    /// Id = 0; if the earliest In happened to be one of those, every such shift would anchor to
    /// the same 0 and collide. Prefer the earliest In with a real (non-zero) id, falling back to 0
    /// only when no real punch exists (e.g. a shift built entirely of synthetic pairs).
    ///
    /// Deliberately NOT memoized: C# record `with` copies private fields verbatim, so a cached
    /// field populated on one instance but not an identical copy would break value-equality
    /// between them. This is cheap enough to just recompute.
    /// </summary>
    public int AnchorPunchId
    {
        get
        {
            var inPunchesByTime = PunchPairs
                .Where(p => p.HasInPunch)
                .OrderBy(p => p.InPunch!.EffectiveTime)
                .Select(p => p.InPunch!)
                .ToList();

            return inPunchesByTime.FirstOrDefault(p => p.Id != 0)?.Id
                ?? inPunchesByTime.FirstOrDefault()?.Id
                ?? 0;
        }
    }
}
