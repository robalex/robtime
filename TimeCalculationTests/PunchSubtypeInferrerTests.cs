using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PunchSubtypeInferrerTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static Instant At(int hour, int minute = 0) => Instant.FromUtc(2023, 1, 2, hour, minute);

    private Punch In(Instant t, PunchSubtype? subtype = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.In, _emp) with { Subtype = subtype };

    private Punch Out(Instant t, PunchSubtype? subtype = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.Out, _emp) with { Subtype = subtype };

    private static IReadOnlyList<Punch> Run(IReadOnlyList<Punch> punches, PayRule? rule = null)
        => PunchSubtypeInferrer.Execute(punches, TestEntityCreator.CreateContext(rule));

    [Fact]
    public void ShortMidShiftGap_ClassifiedAsBreak()
    {
        // 9–12, 15-min gap, 12:15–17.  Default expected: break 15 min, lunch 30 min.
        var punches = new[] { In(At(9)), Out(At(12)), In(At(12, 15)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.None,  result[0].Subtype);
        Assert.Equal(PunchSubtype.Break, result[1].Subtype);
        Assert.Equal(PunchSubtype.Break, result[2].Subtype);
        Assert.Equal(PunchSubtype.None,  result[3].Subtype);
    }

    [Fact]
    public void LongerMidShiftGap_ClassifiedAsLunch()
    {
        // 35-min gap — closer to the 30-min expected lunch than the 15-min break
        var punches = new[] { In(At(9)), Out(At(12)), In(At(12, 35)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.Lunch, result[1].Subtype);
        Assert.Equal(PunchSubtype.Lunch, result[2].Subtype);
    }

    [Fact]
    public void GapEquidistantFromBoth_TieGoesToBreak()
    {
        // 22.5 min is equidistant from 15 and 30; use 22 (closer to break) and
        // 23 (closer to lunch) to pin the boundary
        var closer = Run([In(At(9)), Out(At(12)), In(At(12, 22)), Out(At(17))]);
        Assert.Equal(PunchSubtype.Break, closer[1].Subtype);

        var farther = Run([In(At(9)), Out(At(12)), In(At(12, 23)), Out(At(17))]);
        Assert.Equal(PunchSubtype.Lunch, farther[1].Subtype);
    }

    [Fact]
    public void GapExceedingDistanceBetweenShifts_IsShiftBoundary_NotClassified()
    {
        // 7-hr gap > default DistanceBetweenShiftsHours (6) — two shifts, no break
        var punches = new[] { In(At(1)), Out(At(5)), In(At(12)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.None, result[1].Subtype);
        Assert.Equal(PunchSubtype.None, result[2].Subtype);
    }

    [Fact]
    public void ForcedSubtypeOnOut_IsKept_AndPropagatesToIn()
    {
        // 15-min gap would infer Break, but the Out was forced to Lunch
        var punches = new[] { In(At(9)), Out(At(12), PunchSubtype.Lunch), In(At(12, 15)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.Lunch, result[1].Subtype);
        Assert.Equal(PunchSubtype.Lunch, result[2].Subtype);
    }

    [Fact]
    public void ForcedSubtypeOnIn_IsKept_AndPropagatesToOut()
    {
        var punches = new[] { In(At(9)), Out(At(12)), In(At(12, 15), PunchSubtype.Lunch), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.Lunch, result[1].Subtype);
        Assert.Equal(PunchSubtype.Lunch, result[2].Subtype);
    }

    [Fact]
    public void ForcedNone_SuppressesInference()
    {
        // Supervisor says this gap is not a break — both sides resolve to None
        var punches = new[] { In(At(9)), Out(At(12), PunchSubtype.None), In(At(12, 15)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.None, result[1].Subtype);
        Assert.Equal(PunchSubtype.None, result[2].Subtype);
    }

    [Fact]
    public void CustomExpectedLengths_AreRespected()
    {
        // Expected break 10, lunch 60 — a 30-min gap is closer to the break
        var rule = new PayRule { ExpectedBreakLengthMinutes = 10, ExpectedLunchLengthMinutes = 60 };
        var punches = new[] { In(At(9)), Out(At(12)), In(At(12, 30)), Out(At(17)) };
        var result = Run(punches, rule);

        Assert.Equal(PunchSubtype.Break, result[1].Subtype);
    }

    [Fact]
    public void MultipleGapsInOneShift_EachClassifiedIndependently()
    {
        // 9–10:30 | 15-min break | 10:45–12 | 30-min lunch | 12:30–17
        var punches = new[]
        {
            In(At(9)), Out(At(10, 30)),
            In(At(10, 45)), Out(At(12)),
            In(At(12, 30)), Out(At(17)),
        };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.None,  result[0].Subtype);
        Assert.Equal(PunchSubtype.Break, result[1].Subtype);
        Assert.Equal(PunchSubtype.Break, result[2].Subtype);
        Assert.Equal(PunchSubtype.Lunch, result[3].Subtype);
        Assert.Equal(PunchSubtype.Lunch, result[4].Subtype);
        Assert.Equal(PunchSubtype.None,  result[5].Subtype);
    }

    [Fact]
    public void FixedEntries_PassThroughUntouched()
    {
        var fixedDollar = TestEntityCreator.CreateTestPunch(At(10), PunchKind.FixedDollar, _emp);
        var fixedHours  = TestEntityCreator.CreateTestPunch(At(11), PunchKind.FixedHours,  _emp);
        var result = Run([fixedDollar, fixedHours]);

        Assert.Null(result[0].Subtype);
        Assert.Null(result[1].Subtype);
    }

    [Fact]
    public void FixedEntryBetweenOutAndIn_DoesNotBreakGapDetection()
    {
        // A FixedDollar punch lands inside the break window; the Out→In gap
        // must still be recognized and classified
        var punches = new[]
        {
            In(At(9)), Out(At(12)),
            TestEntityCreator.CreateTestPunch(At(12, 5), PunchKind.FixedDollar, _emp),
            In(At(12, 15)), Out(At(17)),
        };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.Break, result[1].Subtype);
        Assert.Equal(PunchSubtype.Break, result[3].Subtype);
    }

    [Fact]
    public void ConsecutiveIns_NoGapClassification()
    {
        // In followed by In (orphan scenario) — nothing to classify
        var punches = new[] { In(At(9)), In(At(10)), Out(At(17)) };
        var result = Run(punches);

        Assert.Equal(PunchSubtype.None, result[0].Subtype);
        Assert.Equal(PunchSubtype.None, result[1].Subtype);
        Assert.Equal(PunchSubtype.None, result[2].Subtype);
    }

    [Fact]
    public void AllClockPunches_LeaveStageWithResolvedSubtype()
    {
        var punches = new[] { In(At(9)), Out(At(12)), In(At(12, 15)), Out(At(17)) };
        var result = Run(punches);

        Assert.All(result, p => Assert.NotNull(p.Subtype));
    }
}
