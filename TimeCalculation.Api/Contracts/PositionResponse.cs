using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public sealed record PositionResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required decimal BaseRate { get; init; }

    public static PositionResponse FromEntity(Position position) => new()
    {
        Id = position.Id,
        ClientId = position.ClientId,
        Code = position.Code,
        Name = position.Name,
        BaseRate = position.BaseRate,
    };
}
