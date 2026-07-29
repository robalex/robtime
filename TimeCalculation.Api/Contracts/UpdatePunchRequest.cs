using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Every field optional — omitted means "leave the existing punch's value alone," same partial-patch
/// semantics as UpdatePayRuleRequest (a human editing a punch typically corrects one field, e.g. the
/// punch time, and shouldn't have to resend the whole row to do it). No EmployeeId — moving a punch
/// to a different employee isn't an edit, it's delete-and-recreate. No DeviceId/DevicePunchId — a
/// punch's device provenance is a historical fact about how it was ingested, not something a manual
/// edit changes.
///
/// Optional Reason travels alongside the edited fields (not part of them) — it's metadata about the
/// edit itself, written to the PunchAuditEntry, never onto the Punch row.
/// </summary>
public sealed record UpdatePunchRequest
{
    public Instant? PunchTime { get; init; }
    public string? PunchTimeZoneId { get; init; }
    public PunchKind? Kind { get; init; }
    public PunchSubtype? Subtype { get; init; }
    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Hours { get; init; }
    public BonusKind? BonusKind { get; init; }
    public bool? CountsTowardRegularRate { get; init; }

    public string? Reason { get; init; }
}
