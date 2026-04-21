using TimeCalculation.Model;

namespace TimeCalculation.PunchPairing;

public interface IShiftBuilder
{
    List<Shift> CreateShifts(List<PunchPair> punchPairs, PayRule payRule);
}
