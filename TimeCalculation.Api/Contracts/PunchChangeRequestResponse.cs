using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public sealed record PunchChangeRequestResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required int EmployeeId { get; init; }
    public int? PunchId { get; init; }
    public required PunchChangeKind ChangeKind { get; init; }

    public Instant? RequestedPunchTime { get; init; }
    public string? RequestedPunchTimeZoneId { get; init; }
    public PunchKind? RequestedKind { get; init; }
    public PunchSubtype? RequestedSubtype { get; init; }
    public int? RequestedPositionId { get; init; }
    public decimal? RequestedAmount { get; init; }
    public decimal? RequestedHours { get; init; }
    public BonusKind? RequestedBonusKind { get; init; }
    public bool? RequestedCountsTowardRegularRate { get; init; }

    public required string RequesterUserId { get; init; }
    public required string Reason { get; init; }
    public required Instant CreatedAt { get; init; }

    public required PunchChangeRequestStatus Status { get; init; }
    public string? ReviewerUserId { get; init; }
    public Instant? ReviewedAt { get; init; }
    public string? ReviewNote { get; init; }

    // Not `required`: Submit/Decide return the request right after mutating it and have no reason to
    // pay for this lookup, so they call FromEntity with these omitted (null). Only List/Get — the
    // review-queue reads — enrich, via PunchChangeRequestService.EnrichAsync.
    public string? EmployeeFirstName { get; init; }
    public string? EmployeeLastName { get; init; }

    /// <summary>The punch as it exists today, for Edit/Delete requests — null for Add (nothing to
    /// compare against yet) and null if the target punch is gone. Lets a reviewer see what they'd
    /// actually be changing/removing, not just the requested new values in isolation.</summary>
    public PunchResponse? CurrentPunch { get; init; }

    public static PunchChangeRequestResponse FromEntity(
        PunchChangeRequest request, Employee? employee = null, Punch? currentPunch = null) => new()
    {
        Id = request.Id,
        ClientId = request.ClientId,
        EmployeeId = request.EmployeeId,
        PunchId = request.PunchId,
        ChangeKind = request.ChangeKind,
        RequestedPunchTime = request.RequestedPunchTime,
        RequestedPunchTimeZoneId = request.RequestedPunchTimeZoneId,
        RequestedKind = request.RequestedKind,
        RequestedSubtype = request.RequestedSubtype,
        RequestedPositionId = request.RequestedPositionId,
        RequestedAmount = request.RequestedAmount,
        RequestedHours = request.RequestedHours,
        RequestedBonusKind = request.RequestedBonusKind,
        RequestedCountsTowardRegularRate = request.RequestedCountsTowardRegularRate,
        RequesterUserId = request.RequesterUserId,
        Reason = request.Reason,
        CreatedAt = request.CreatedAt,
        Status = request.Status,
        ReviewerUserId = request.ReviewerUserId,
        ReviewedAt = request.ReviewedAt,
        ReviewNote = request.ReviewNote,
        EmployeeFirstName = employee?.FirstName,
        EmployeeLastName = employee?.LastName,
        CurrentPunch = currentPunch is not null ? PunchResponse.FromEntity(currentPunch) : null,
    };
}
