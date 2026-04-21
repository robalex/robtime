using TimeCalculation.Model;

namespace TimeCalculation.PunchPairing;

public class PunchPairer : IPunchPairer
{
    public List<PunchPair> PairPunches(List<Punch> punches, PayRule payRule)
    {
        if (punches == null || !punches.Any()) return new();

        var ordered = punches.OrderBy(p => p.PunchTime).ToList();
        var pairs = new List<PunchPair>();
        Punch? inPunch = null;

        foreach (var punch in ordered) {
            if (punch.PunchType == PunchType.In) {
                inPunch = punch;
            } else if (punch.PunchType == PunchType.Out && inPunch != null) {
                var duration = (punch.PunchTime - inPunch.PunchTime).TotalHours;

                if (duration > payRule.MaxShiftLengthHours) {
                    // Leave out punch unpaired
                    pairs.Add(new PunchPair
                    {
                        InPunch = inPunch,
                        PairDate = DateOnly.FromDateTime(inPunch.PunchTime.ToDateTimeUtc())
                    });
                    inPunch = punch; // Start new pair
                } else {
                    pairs.Add(new PunchPair
                    {
                        InPunch = inPunch,
                        OutPunch = punch,
                        PairDate = DateOnly.FromDateTime(inPunch.PunchTime.ToDateTimeUtc())
                    });
                    inPunch = null;
                }
            }
        }

        if (inPunch != null) {
            pairs.Add(new PunchPair
            {
                InPunch = inPunch,
                PairDate = DateOnly.FromDateTime(inPunch.PunchTime.ToDateTimeUtc())
            });
        }

        return pairs;
    }
}

