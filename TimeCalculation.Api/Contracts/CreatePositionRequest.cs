namespace TimeCalculation.Api.Contracts;

public record CreatePositionRequest
{
    public required int ClientId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required decimal BaseRate { get; init; }
}
