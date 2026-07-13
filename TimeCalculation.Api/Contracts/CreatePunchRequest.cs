using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public record CreatePunchRequest
{
    public required int EmployeeId { get; init; }
    public required Instant PunchTime { get; init; }
    public string? PunchTimeZoneId { get; init; }
    public required PunchKind Kind { get; init; }

    /// <summary>Force a Break/Lunch classification instead of letting Stage 2 infer it. Leave null
    /// to infer.</summary>
    public PunchSubtype? Subtype { get; init; }

    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }    // required by convention when Kind == FixedDollar
    public decimal? Hours { get; init; }     // required by convention when Kind == FixedHours
    public BonusKind? BonusKind { get; init; }
    public bool CountsTowardRegularRate { get; init; }

    // Device idempotency — omit both if this punch isn't coming from a clock device.
    public string? DeviceId { get; init; }
    public string? DevicePunchId { get; init; }

    /// <summary>Who/what recorded this punch. In a real deployment this comes from the
    /// authenticated caller, not a client-supplied field — there is no auth wired up yet.</summary>
    public required string CreatedBy { get; init; }
}
