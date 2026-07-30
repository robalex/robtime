using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// One interval during which a DifferentialRule *could* apply — independent of any actual punches.
/// Produced by DifferentialZoneProjector for the differential sandbox's calendar view ("where would
/// this rule ever fire"). Distinct from AppliedDifferential, which records what actually happened
/// against real worked time; a zone says nothing about qualification thresholds or exclusivity —
/// those only make sense once real hours are known.
/// </summary>
public record DifferentialZone
{
    public required string Code { get; init; }
    public required Instant Start { get; init; }
    public required Instant End { get; init; }
}
