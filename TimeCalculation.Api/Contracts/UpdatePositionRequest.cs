namespace TimeCalculation.Api.Contracts;

/// <summary>
/// No ClientId — PUT operates on an existing Position identified by the route id, and which
/// client owns it isn't something a caller changes via update.
/// </summary>
public record UpdatePositionRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required decimal BaseRate { get; init; }
}
