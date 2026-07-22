using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Persistence;

/// <summary>
/// Persistence-shape entities for the effective-dated assignment tables.  The pure-domain records
/// (PayRuleAssignment, EmployeePositionAssignment) are immutable value types the calculator consumes;
/// these mutable POCOs are what EF Core maps, keeping the domain free of persistence concerns.
/// </summary>
public class PayRuleAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PayRuleId { get; set; }
    public PayRule PayRule { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }

    public PayRuleAssignment ToDomain() => new(PayRule, EffectiveFrom, EffectiveTo);
}

public class EmployeePositionAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }
    public decimal? Rate { get; set; }

    public EmployeePositionAssignment ToDomain() => new(Position, EffectiveFrom, EffectiveTo, Rate);
}
