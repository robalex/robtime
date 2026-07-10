using TimeCalculation.Model;

namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// Stage 12 — Overtime.  Allocates a workweek's hours via the supplied IOvertimeRule and pairs the
/// allocation with the regular rate of pay (from Stage 11) to produce an OvertimeResult.
/// </summary>
public static class OvertimeCalculator
{
    public static OvertimeResult Calculate(Workweek week, IOvertimeRule rule, decimal regularRate)
        => new() { Allocation = rule.Allocate(week), RegularRate = regularRate };
}
