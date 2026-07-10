using TimeCalculation.Model;

namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// A jurisdiction's overtime policy.  Splits a workweek's hours into regular/overtime/doubletime.
/// Adding a state's rules means implementing this interface — the rest of the engine is unaffected.
/// </summary>
public interface IOvertimeRule
{
    OvertimeAllocation Allocate(Workweek week);
}
