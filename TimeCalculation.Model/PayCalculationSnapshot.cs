using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A frozen PayResult plus the identities/versions of every rule used to produce it, so a
/// calculation can be defended in audit and reproduced exactly.  Append-only; re-running a
/// calculation creates a new snapshot rather than mutating one.
/// </summary>
public record PayCalculationSnapshot
{
    public int EmployeeId { get; init; }
    public Instant CalculatedAt { get; init; }
    public PayResult Result { get; init; } = new();

    /// <summary>(PayRule.Id, PayRule.Version) pairs of every rule that governed the calculation.</summary>
    public IReadOnlyList<(int Id, int Version)> PayRuleVersions { get; init; } = [];

    /// <summary>Position IDs whose rates fed the calculation.</summary>
    public IReadOnlyList<int> PositionIds { get; init; } = [];
}
