using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>No Employee/Position navigation fields — same reasoning as PunchAuditor's snapshot
/// stripping them: a caller of these endpoints wants this punch's own data, not whatever the query
/// happened to eager-load.</summary>
public sealed record PunchResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required int EmployeeId { get; init; }
    public required Instant PunchTime { get; init; }
    public Instant? RoundedPunchTime { get; init; }
    public required string PunchTimeZoneId { get; init; }
    public required PunchKind Kind { get; init; }
    public PunchSubtype? Subtype { get; init; }
    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Hours { get; init; }
    public BonusKind? BonusKind { get; init; }
    public required bool CountsTowardRegularRate { get; init; }
    public required Instant CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public string? DeviceId { get; init; }
    public string? DevicePunchId { get; init; }

    public static PunchResponse FromEntity(Punch punch) => new()
    {
        Id = punch.Id,
        ClientId = punch.ClientId,
        EmployeeId = punch.EmployeeId,
        PunchTime = punch.PunchTime,
        RoundedPunchTime = punch.RoundedPunchTime,
        PunchTimeZoneId = punch.PunchTimeZoneId,
        Kind = punch.Kind,
        Subtype = punch.Subtype,
        PositionId = punch.PositionId,
        Amount = punch.Amount,
        Hours = punch.Hours,
        BonusKind = punch.BonusKind,
        CountsTowardRegularRate = punch.CountsTowardRegularRate,
        CreatedAt = punch.CreatedAt,
        CreatedBy = punch.CreatedBy,
        DeviceId = punch.DeviceId,
        DevicePunchId = punch.DevicePunchId,
    };
}
