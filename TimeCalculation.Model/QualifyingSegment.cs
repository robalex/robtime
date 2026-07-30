using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// An actual qualifying interval — where a DifferentialRule was both active (day schedule) and
/// inside its time-of-day window, intersected with real worked time. Produced by the per-pair
/// qualifying-hours calculators (PerDayQualifyingHoursCalculator.Segments /
/// ContinuousRangeQualifyingHoursCalculator.Segments) for the differential sandbox's explainer.
/// Distinct from DifferentialZone, which shows where a rule *could* apply independent of any
/// punches — this shows what actually happened.
/// </summary>
public record QualifyingSegment
{
    public required Instant Start { get; init; }
    public required Instant End { get; init; }
}
