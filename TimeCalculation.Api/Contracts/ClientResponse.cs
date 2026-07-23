using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public sealed record ClientResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedDate { get; init; }

    public static ClientResponse FromEntity(Client client) => new()
    {
        Id = client.Id,
        Name = client.Name,
        CreatedBy = client.CreatedBy,
        CreatedDate = client.CreatedDate,
    };
}
