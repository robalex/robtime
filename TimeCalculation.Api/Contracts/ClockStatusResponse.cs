using NodaTime;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Whether this employee is currently on the clock — what the self-service clock button renders
/// itself from (UI_PLAN.md Phase 6.4). Server-owned rather than derived in the browser so the
/// "am I clocked in" rule lives in one place, and so the kiosk clock can reuse it verbatim when
/// device/badge auth lands (§11).
/// </summary>
public sealed record ClockStatusResponse
{
    public required int EmployeeId { get; init; }
    public required bool IsClockedIn { get; init; }

    /// <summary>When the open In punch happened; null when clocked out.</summary>
    public Instant? Since { get; init; }

    /// <summary>Position on the open In punch, when it carried one — lets the UI label the running
    /// shift ("Cook · 4h 18m") without a second lookup. Null when clocked out, or when the punch
    /// didn't name a position.</summary>
    public int? PositionId { get; init; }

    /// <summary>Id of the punch that put them on the clock — null when clocked out. Useful for a
    /// client that wants to link straight to correcting it.</summary>
    public int? SincePunchId { get; init; }
}
