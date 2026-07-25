using NodaTime;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public record PositionAssignmentResponse
{
    public required int Id { get; init; }
    public required int EmployeeId { get; init; }
    public required int PositionId { get; init; }

    /// <summary>Denormalised for display — a timeline showing "Cook" beats one showing "position 7",
    /// and fetching positions separately just to label rows is a round trip for nothing.</summary>
    public required string PositionCode { get; init; }
    public required string PositionName { get; init; }

    public required LocalDate EffectiveFrom { get; init; }

    /// <summary>Null means still in effect.</summary>
    public LocalDate? EffectiveTo { get; init; }

    /// <summary>
    /// This employee's own rate for this assignment, when it differs from the position's BaseRate.
    /// Null means "use the position's rate" — the engine's own fallback chain (see
    /// PipelineContext.GetRateAt), not a missing value.
    /// </summary>
    public decimal? Rate { get; init; }

    /// <summary>The position's BaseRate, so the UI can show what null Rate resolves to without a
    /// second lookup.</summary>
    public required decimal PositionBaseRate { get; init; }

    public static PositionAssignmentResponse FromEntity(EmployeePositionAssignmentEntity entity) => new()
    {
        Id = entity.Id,
        EmployeeId = entity.EmployeeId,
        PositionId = entity.PositionId,
        PositionCode = entity.Position.Code,
        PositionName = entity.Position.Name,
        EffectiveFrom = entity.EffectiveFrom,
        EffectiveTo = entity.EffectiveTo,
        Rate = entity.Rate,
        PositionBaseRate = entity.Position.BaseRate,
    };
}

public record CreatePositionAssignmentRequest
{
    public required int PositionId { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }

    /// <summary>Omit to use the position's BaseRate.</summary>
    public decimal? Rate { get; init; }
}

public record UpdatePositionAssignmentRequest
{
    public required int PositionId { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public decimal? Rate { get; init; }
}
