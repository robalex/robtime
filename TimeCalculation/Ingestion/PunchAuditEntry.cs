using NodaTime;

namespace TimeCalculation.Ingestion;

public record PunchAuditEntry
{
    public int Id { get; init; }
    public int PunchId { get; init; }
    public int ActorUserId { get; init; }
    public Instant OccurredAt { get; init; }

    /// <summary>"Created", "Edited", or "Deleted".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>JSON snapshot of the punch state before the change. Null on creation.</summary>
    public string? PreviousValues { get; init; }

    /// <summary>JSON snapshot of the punch state after the change. Null on deletion.</summary>
    public string? NewValues { get; init; }

    public string? Reason { get; init; }
}
