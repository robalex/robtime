using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public interface IWeightedOvertimeCalculator
{
    double CalculateOvertime(Week week, Employee employee);
}

