using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public class WeightedOvertimeCalculator : IWeightedOvertimeCalculator
{
    public double CalculateOvertime(Week week, Employee employee)
    {
        var totalHours = week.Shifts.Sum(s => s.PunchPairs.Sum(p => p.TotalHours));
        if (totalHours <= 40) return 0;

        var overtime = totalHours - 40;
        var rate = employee.MinimumWage * 1.5f; // Example: 1.5x
        return overtime * rate;
    }
}

