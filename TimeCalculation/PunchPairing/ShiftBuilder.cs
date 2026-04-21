using TimeCalculation.Model;
using static TimeCalculation.PunchPairing.ShiftBuilder;

namespace TimeCalculation.PunchPairing;

public class ShiftBuilder : IShiftBuilder
{
    public List<Shift> CreateShifts(List<PunchPair> punchPairs, PayRule payRule)
    {
        if (punchPairs == null || !punchPairs.Any()) {
            return [];
        }

        // Order by InPunch time if it exists, otherwise by OutPunch time
        var orderedPairs = punchPairs
            .OrderBy(p => p.InPunch != null ? p.InPunch.PunchTime : p.OutPunch?.PunchTime)
            .ToList();

        var shifts = new List<Shift>();
        var currentShift = new Shift { PunchPairs = [] };

        foreach (var pair in orderedPairs) {
            if (currentShift.PunchPairs.Count == 0) {
                currentShift.PunchPairs.Add(pair);
                continue;
            }

            var lastOutPunch = currentShift.PunchPairs.Last().OutPunch;
            var pairTime = pair.InPunch?.PunchTime ?? pair.OutPunch?.PunchTime;
            if (ShouldCreateNewShift(payRule, lastOutPunch, pairTime)) {
                shifts.Add(currentShift);
                currentShift = new Shift { PunchPairs = [pair] };
            }

            currentShift.PunchPairs.Add(pair);
        }

        if (currentShift.PunchPairs.Count > 0) {
            shifts.Add(currentShift);
        }

        return shifts;
    }

    private static bool ShouldCreateNewShift(PayRule payRule, Punch? lastOutPunch, NodaTime.Instant? pairTime)
    {
        return lastOutPunch == null || pairTime == null || (pairTime - lastOutPunch.PunchTime).Value.TotalHours > payRule.DistanceBetweenShiftsHours;
    }
}

