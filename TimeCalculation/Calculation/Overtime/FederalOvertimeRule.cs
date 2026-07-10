using TimeCalculation.Model;

namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// Federal FLSA overtime: hours beyond the weekly threshold (default 40) are paid at 1.5×.
/// No daily or doubletime tiers.
/// </summary>
public class FederalOvertimeRule : IOvertimeRule
{
    private readonly decimal _weeklyThreshold;

    public FederalOvertimeRule(decimal weeklyThreshold = 40m) => _weeklyThreshold = weeklyThreshold;

    public OvertimeAllocation Allocate(Workweek week)
    {
        var total = week.TotalHours;
        var overtime = Math.Max(0, total - _weeklyThreshold);
        return new OvertimeAllocation { RegularHours = total - overtime, OvertimeHours = overtime };
    }
}
