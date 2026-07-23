namespace TimeCalculation.Api.Contracts;

/// <summary>No CreatedBy/CreatedDate — those are immutable audit facts about creation, not
/// something a caller updates.</summary>
public record UpdateClientRequest
{
    public required string Name { get; init; }
}
