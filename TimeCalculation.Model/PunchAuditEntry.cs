using NodaTime;

namespace TimeCalculation.Model;

/// <summary>Immutable record of one create/edit/delete of a Punch, for audit trail purposes.</summary>
public record PunchAuditEntry
{
    public int Id { get; init; }
    public int ClientId { get; init; }
    public int PunchId { get; init; }
    /// <summary>Cognito `sub` claim of the authenticated principal who made the change — a plain
    /// string, not a foreign key lookup, since the JWT already carries it (see AppUser.CognitoSub in
    /// TimeCalculation.Persistence and UI_PLAN.md §5's "claims carry client_id/role" design).</summary>
    public string ActorUserId { get; init; } = string.Empty;
    public Instant OccurredAt { get; init; }

    /// <summary>"Created", "Edited", or "Deleted".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>JSON snapshot of the punch state before the change. Null on creation.</summary>
    public string? PreviousValues { get; init; }

    /// <summary>JSON snapshot of the punch state after the change. Null on deletion.</summary>
    public string? NewValues { get; init; }

    public string? Reason { get; init; }
}
