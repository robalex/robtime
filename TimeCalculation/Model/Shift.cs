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
}
