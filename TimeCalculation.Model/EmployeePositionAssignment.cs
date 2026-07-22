using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// Associates an employee with a Position for a given date range.
/// EffectiveTo of null means the assignment is still active.
/// </summary>
/// <param name="Rate">
/// This employee's own rate while under this assignment, when it differs from
/// <see cref="Position.BaseRate"/> (e.g. two employees in the same job code paid differently).
/// Null means "use the Position's BaseRate" — resolved by the pipeline's rate lookup, which falls
/// back to BaseRate and then the employee's minimum wage when no position is found at all.
/// </param>
public record EmployeePositionAssignment(
    Position Position,
    LocalDate EffectiveFrom,
    LocalDate? EffectiveTo = null,
    decimal? Rate = null
);
