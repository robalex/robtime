using TimeCalculation.Model;

namespace TimeCalculation.PunchPairing
{
    public interface IPunchPairer
    {
        List<PunchPair> PairPunches(List<Punch> punches, PayRule payRule);
    }
}
