using NodaTime;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// A closed-open-ended effective date range: <see cref="To"/> null means "still in effect".
/// Inclusive at both ends when To is set — an assignment ending 2026-07-31 covers that whole day.
/// </summary>
public sealed record DateRange(LocalDate From, LocalDate? To)
{
    /// <summary>
    /// Whether two ranges share any day. Null <see cref="To"/> is treated as unbounded, so an
    /// open-ended assignment overlaps everything at or after its start.
    ///
    /// Pure and DB-free so the rule can be unit-tested against the awkward cases (touching
    /// boundaries, two open-ended ranges, one range wholly inside another) without a database —
    /// the service only supplies the existing ranges to compare against.
    /// </summary>
    public bool Overlaps(DateRange other)
    {
        var startsBeforeOtherEnds = other.To is null || From <= other.To;
        var otherStartsBeforeThisEnds = To is null || other.From <= To;
        return startsBeforeOtherEnds && otherStartsBeforeThisEnds;
    }

    /// <summary>A range is well-formed when it doesn't end before it begins.</summary>
    public bool IsWellFormed => To is null || To >= From;
}
