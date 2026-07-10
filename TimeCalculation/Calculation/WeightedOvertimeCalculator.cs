using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public class WeightedOvertimeCalculator : IWeightedOvertimeCalculator
{
    public decimal CalculateOvertime(Week week, Employee employee)
    {
        var totalHours = week.Shifts.Sum(s => s.PunchPairs.Sum(p => p.TotalHours));
        if (totalHours <= 40) return 0;

        var overtimeHours = totalHours - 40;
        // Spread non-discretionary bonus across all hours to derive the weighted regular rate,
        // then return only the half-time premium (straight time already paid by the caller).
        var weightedRate = (totalHours * employee.MinimumWage + week.NonDiscretionaryBonus) / totalHours;
        return 0.5m * weightedRate * overtimeHours;
    }
}
