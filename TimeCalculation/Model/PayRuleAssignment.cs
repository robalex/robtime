using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// Links a PayRule to an employee for a specific date range.
/// EffectiveTo of null means the assignment is still active.
/// </summary>
public record PayRuleAssignment(
    PayRule PayRule,
    LocalDate EffectiveFrom,
    LocalDate? EffectiveTo = null
);
