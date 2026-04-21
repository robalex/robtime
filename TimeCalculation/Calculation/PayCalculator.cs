using TimeCalculation.Model;
using TimeCalculation.PunchPairing;

namespace TimeCalculation.Calculation;

public class PayCalculator : IPayCalculator
{
    private readonly IPunchPairer _punchPairer;
    private readonly IShiftBuilder _shiftBuilder;
    private readonly IWeightedOvertimeCalculator _overtimeCalculator;

    public PayCalculator(
        IPunchPairer punchPairer,
        IShiftBuilder shiftBuilder,
        IWeightedOvertimeCalculator overtimeCalculator)
    {
        _punchPairer = punchPairer;
        _shiftBuilder = shiftBuilder;
        _overtimeCalculator = overtimeCalculator;
    }

    public double CalculatePay(List<Punch> punches, PayRule rule, Employee employee)
    {
        var pairs = _punchPairer.PairPunches(punches, rule);
        var shifts = _shiftBuilder.CreateShifts(pairs, rule);

        var totalHours = pairs.Sum(p => p.TotalHours);
        var basePay = totalHours * employee.MinimumWage;
        var week = new Week { Shifts = shifts };
        var overtime = _overtimeCalculator.CalculateOvertime(week, employee);

        return basePay + overtime;
    }
}

