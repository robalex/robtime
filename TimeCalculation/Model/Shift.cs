using NodaTime;

namespace TimeCalculation.Model;

public record Shift
{
    public IReadOnlyList<PunchPair> PunchPairs { get; init; } = [];
    public IReadOnlyList<Punch> FixedEntries { get; init; } = [];
    public IReadOnlyList<AppliedDifferential> Differentials { get; init; } = [];
    public LocalDate ShiftDate { get; init; }

    public decimal TotalHours => PunchPairs.Sum(p => p.TotalHours);
}
