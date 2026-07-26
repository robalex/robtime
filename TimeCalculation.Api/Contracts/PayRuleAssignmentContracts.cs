using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public record PayRuleAssignmentResponse
{
    public required int Id { get; init; }
    public required int EmployeeId { get; init; }
    public required int PayRuleId { get; init; }

    /// <summary>Denormalised for display — same reasoning as PositionAssignmentResponse's
    /// PositionCode/PositionName: a timeline showing "Federal Standard" beats "pay rule 7".</summary>
    public required string PayRuleName { get; init; }
    public required PayRuleStatus PayRuleStatus { get; init; }

    public required LocalDate EffectiveFrom { get; init; }

    /// <summary>Null means still in effect.</summary>
    public LocalDate? EffectiveTo { get; init; }

    public static PayRuleAssignmentResponse FromEntity(PayRuleAssignmentEntity entity) => new()
    {
        Id = entity.Id,
        EmployeeId = entity.EmployeeId,
        PayRuleId = entity.PayRuleId,
        PayRuleName = entity.PayRule.Name,
        PayRuleStatus = entity.PayRule.Status,
        EffectiveFrom = entity.EffectiveFrom,
        EffectiveTo = entity.EffectiveTo,
    };
}

public record CreatePayRuleAssignmentRequest
{
    public required int PayRuleId { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
}

public record UpdatePayRuleAssignmentRequest
{
    public required int PayRuleId { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
}
