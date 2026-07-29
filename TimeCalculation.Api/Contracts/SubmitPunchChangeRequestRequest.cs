using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Same field shape as UpdatePunchRequest (PunchTime through CountsTowardRegularRate), plus the
/// discriminator and targeting fields a change request needs on top: ChangeKind decides which of
/// PunchId/EmployeeId is required (see PunchChangeRequestValidator) and Reason is always required —
/// unlike a direct edit's optional Reason, a request that will sit in someone else's queue needs one.
/// </summary>
public sealed record SubmitPunchChangeRequestRequest
{
    public required PunchChangeKind ChangeKind { get; init; }

    /// <summary>Required for Edit/Delete (the punch being changed); must be omitted for Add.</summary>
    public int? PunchId { get; init; }

    /// <summary>Required for Add (there's no existing punch to derive it from); ignored for
    /// Edit/Delete, where it's always taken from the target punch itself.</summary>
    public int? EmployeeId { get; init; }

    public required string Reason { get; init; }

    public Instant? PunchTime { get; init; }
    public string? PunchTimeZoneId { get; init; }
    public PunchKind? Kind { get; init; }
    public PunchSubtype? Subtype { get; init; }
    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Hours { get; init; }
    public BonusKind? BonusKind { get; init; }
    public bool? CountsTowardRegularRate { get; init; }
}
