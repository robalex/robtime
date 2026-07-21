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

    private static PunchPair Pair(Punch? inPunch, Punch? outPunch) => new() { InPunch = inPunch, OutPunch = outPunch };

    // Runs a single Shift built from the given (already-grouped) pairs through the stage — this
    // stage no longer decides shift boundaries itself, so tests supply pre-grouped Shifts directly
    // rather than relying on ShiftBuilder.
    private static Shift RunShift(IReadOnlyList<PunchPair> pairs, PayRule? rule = null)
    {
        var shift = new Shift { PunchPairs = pairs };
        var ctx = TestEntityCreator.CreateContext(rule);
        return PunchSubtypeInferrer.InferPunchSubtypes([shift], ctx)[0];
    }

    [Fact]
    public void ShortMidShiftGap_ClassifiedAsBreak()
    {
        // 9–12, 15-min gap, 12:15–17.  Default expected: break 15 min, lunch 30 min.
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 15)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.None,  result.PunchPairs[0].InPunch!.Subtype);
        Assert.Equal(PunchSubtype.Break, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Break, result.PunchPairs[1].InPunch!.Subtype);
        Assert.Equal(PunchSubtype.None,  result.PunchPairs[1].OutPunch!.Subtype);
    }

    [Fact]
    public void LongerMidShiftGap_ClassifiedAsLunch()
    {
        // 35-min gap — closer to the 30-min expected lunch than the 15-min break
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 35)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void GapEquidistantFromBoth_TieGoesToBreak()
    {
        // 22.5 min is equidistant from 15 and 30; use 22 (closer to break) and
        // 23 (closer to lunch) to pin the boundary
        var closer = RunShift([Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 22)), Out(At(17)))]);
        Assert.Equal(PunchSubtype.Break, closer.PunchPairs[0].OutPunch!.Subtype);

        var farther = RunShift([Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 23)), Out(At(17)))]);
        Assert.Equal(PunchSubtype.Lunch, farther.PunchPairs[0].OutPunch!.Subtype);
    }

    [Fact]
    public void PairsInDifferentShifts_AreNeverComparedAcrossShifts()
    {
        // Shift boundaries are ShiftBuilder's decision (Stage 4), made before this stage runs — a
        // gap that spans two Shifts is never even visible to this stage as a candidate pair.
        var shiftA = new Shift { PunchPairs = [Pair(In(At(1)), Out(At(5)))] };
        var shiftB = new Shift { PunchPairs = [Pair(In(At(12)), Out(At(17)))] };
        var ctx = TestEntityCreator.CreateContext();

        var result = PunchSubtypeInferrer.InferPunchSubtypes([shiftA, shiftB], ctx);

        Assert.Equal(PunchSubtype.None, result[0].PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.None, result[1].PunchPairs[0].InPunch!.Subtype);
    }

    [Fact]
    public void SplitBoundaryZeroGap_NotClassified()
    {
        // PunchPairer.SplitAtBoundaries produces back-to-back pairs sharing the exact same instant
        // (no real gap) when a pair spans a rule/date boundary. That must not be mistaken for a break.
        var boundary = At(12);
        var pairs = new[] { Pair(In(At(9)), Out(boundary)), Pair(In(boundary), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.None, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.None, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void ForcedSubtypeOnOut_IsKept_AndPropagatesToIn()
    {
        // 15-min gap would infer Break, but the Out was forced to Lunch
        var pairs = new[] { Pair(In(At(9)), Out(At(12), PunchSubtype.Lunch)), Pair(In(At(12, 15)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void ForcedSubtypeOnIn_IsKept_AndPropagatesToOut()
    {
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 15), PunchSubtype.Lunch), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void ForcedNone_SuppressesInference()
    {
        // Supervisor says this gap is not a break — both sides resolve to None
        var pairs = new[] { Pair(In(At(9)), Out(At(12), PunchSubtype.None)), Pair(In(At(12, 15)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.None, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.None, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void CustomExpectedLengths_AreRespected()
    {
        // Expected break 10, lunch 60 — a 30-min gap is closer to the break
        var rule = new PayRule { ExpectedBreakLengthMinutes = 10, ExpectedLunchLengthMinutes = 60 };
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 30)), Out(At(17))) };
        var result = RunShift(pairs, rule);

        Assert.Equal(PunchSubtype.Break, result.PunchPairs[0].OutPunch!.Subtype);
    }

    [Fact]
    public void MultipleGapsInOneShift_EachClassifiedIndependently()
    {
        // 9–10:30 | 15-min break | 10:45–12 | 30-min lunch | 12:30–17
        var pairs = new[]
        {
            Pair(In(At(9)), Out(At(10, 30))),
            Pair(In(At(10, 45)), Out(At(12))),
            Pair(In(At(12, 30)), Out(At(17))),
        };
        var result = RunShift(pairs);

        Assert.Equal(PunchSubtype.None,  result.PunchPairs[0].InPunch!.Subtype);
        Assert.Equal(PunchSubtype.Break, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Break, result.PunchPairs[1].InPunch!.Subtype);
        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[1].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Lunch, result.PunchPairs[2].InPunch!.Subtype);
        Assert.Equal(PunchSubtype.None,  result.PunchPairs[2].OutPunch!.Subtype);
    }

    [Fact]
    public void FixedEntries_PassThroughUntouched()
    {
        var fixedDollar = TestEntityCreator.CreateTestPunch(At(10), PunchKind.FixedDollar, _emp);
        var fixedHours  = TestEntityCreator.CreateTestPunch(At(11), PunchKind.FixedHours,  _emp);
        var shift = new Shift { FixedEntries = [fixedDollar, fixedHours] };
        var ctx = TestEntityCreator.CreateContext();

        var result = PunchSubtypeInferrer.InferPunchSubtypes([shift], ctx)[0];

        Assert.Null(result.FixedEntries[0].Subtype);
        Assert.Null(result.FixedEntries[1].Subtype);
    }

    [Fact]
    public void FixedEntryBetweenOutAndIn_DoesNotBreakGapDetection()
    {
        // A FixedDollar entry attached to the shift (but not part of PunchPairs) must not
        // interfere with classifying the surrounding Out->In gap.
        var fixedDollar = TestEntityCreator.CreateTestPunch(At(12, 5), PunchKind.FixedDollar, _emp);
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 15)), Out(At(17))) };
        var shift = new Shift { PunchPairs = pairs, FixedEntries = [fixedDollar] };
        var ctx = TestEntityCreator.CreateContext();

        var result = PunchSubtypeInferrer.InferPunchSubtypes([shift], ctx)[0];

        Assert.Equal(PunchSubtype.Break, result.PunchPairs[0].OutPunch!.Subtype);
        Assert.Equal(PunchSubtype.Break, result.PunchPairs[1].InPunch!.Subtype);
    }

    [Fact]
    public void OrphanInWithNoOut_NoGapClassification()
    {
        // An orphan In (no Out) followed by a complete pair — nothing to classify against it
        var pairs = new[] { Pair(In(At(9)), null), Pair(In(At(10)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.Null(result.PunchPairs[0].InPunch!.Subtype);
        Assert.Null(result.PunchPairs[1].InPunch!.Subtype);
        Assert.Null(result.PunchPairs[1].OutPunch!.Subtype);
    }

    [Fact]
    public void AllClockPunches_LeaveStageWithResolvedSubtype()
    {
        var pairs = new[] { Pair(In(At(9)), Out(At(12))), Pair(In(At(12, 15)), Out(At(17))) };
        var result = RunShift(pairs);

        Assert.All(result.PunchPairs, p =>
        {
            Assert.NotNull(p.InPunch!.Subtype);
            Assert.NotNull(p.OutPunch!.Subtype);
        });
    }
}
