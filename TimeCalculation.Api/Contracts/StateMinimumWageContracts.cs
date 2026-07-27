using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public record CreateStateMinimumWageRequest
{
    public required string State { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public required decimal Amount { get; init; }
}

public record UpdateStateMinimumWageRequest
{
    public required string State { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record StateMinimumWageResponse
{
    public required int Id { get; init; }
    public required string State { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public required decimal Amount { get; init; }

    public static StateMinimumWageResponse FromEntity(StateMinimumWage wage) => new()
    {
        Id = wage.Id,
        State = wage.State,
        EffectiveFrom = wage.EffectiveFrom,
        EffectiveTo = wage.EffectiveTo,
        Amount = wage.Amount,
    };
}
