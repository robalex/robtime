using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public interface IPayCalculator
{
    double CalculatePay(List<Punch> punches, PayRule rule, Employee employee);
}
