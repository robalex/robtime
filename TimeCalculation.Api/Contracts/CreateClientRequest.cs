namespace TimeCalculation.Api.Contracts;

public record CreateClientRequest
{
    public required string Name { get; init; }
}
