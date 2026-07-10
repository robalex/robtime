using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public interface IWeightedOvertimeCalculator
{
    decimal CalculateOvertime(Week week, Employee employee);
}
