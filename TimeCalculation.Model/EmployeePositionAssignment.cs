using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// Associates an employee with a Position (and its rate) for a given date range.
/// EffectiveTo of null means the assignment is still active.
/// </summary>
public record EmployeePositionAssignment(
    Position Position,
    LocalDate EffectiveFrom,
    LocalDate? EffectiveTo = null
);
