using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.ShiftBuilding;
using Xunit;

namespace TimeCalculationTests;

public class ShiftBuilderTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static Instant At(int dayOffset, int hour) => Instant.FromUtc(2023, 1, 2 + dayOffset, hour, 0);

    private PunchPair MakePair(Instant inTime, Instant outTime)
    {
        var inP  = TestEntityCreator.CreateTestPunch(inTime,  PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(outTime, PunchKind.Out, _emp);
        return TestEntityCreator.CreateTestPunchPair(inP, outP);
    }

    [Fact]
    public void NoPairs_ReturnsEmpty()
    {
        var ctx    = TestEntityCreator.CreateContext();
        var result = ShiftBuilder.BuildShifts([], [], ctx);
        Assert.Empty(result);
    }

    [Fact]
    public void SinglePair_ProducesOneShift()
    {
        var ctx    = TestEntityCreator.CreateContext();
        var pair   = MakePair(At(0, 9), At(0, 17));
        var result = ShiftBuilder.BuildShifts([pair], [], ctx);
        Assert.Single(result);
        Assert.Single(result[0].PunchPairs);
    }

    [Fact]
    public void TwoPairsWithShortGap_ProducesOneShift()
    {
        // 1-hr break < default 6-hr DistanceBetweenShiftsHours
        var ctx   = TestEntityCreator.CreateContext();
        var pair1 = MakePair(At(0, 9),  At(0, 13));
        var pair2 = MakePair(At(0, 14), At(0, 18));
        var result = ShiftBuilder.BuildShifts([pair1, pair2], [], ctx);
        Assert.Single(result);
        Assert.Equal(2, result[0].PunchPairs.Count);
    }

    [Fact]
    public void TwoPairsWithLongGap_ProducesTwoShifts()
    {
        // 7-hr break > default 6-hr threshold
        var ctx   = TestEntityCreator.CreateContext();
        var pair1 = MakePair(At(0, 7),  At(0, 11));
        var pair2 = MakePair(At(0, 18), At(0, 22));
        var result = ShiftBuilder.BuildShifts([pair1, pair2], [], ctx);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ConfigurableDistance_IsRespected()
    {
        // Set threshold to 2 hrs; 3-hr gap should create a new shift
        var rule = new PayRule { DistanceBetweenShiftsHours = 2 };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var pair1 = MakePair(At(0, 9),  At(0, 12));
        var pair2 = MakePair(At(0, 15), At(0, 19));
        var result = ShiftBuilder.BuildShifts([pair1, pair2], [], ctx);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FixedEntry_AttachedToNearestShift()
    {
        var ctx = TestEntityCreator.CreateContext();
        var pair = MakePair(At(0, 9), At(0, 17));
        var bonus = TestEntityCreator.CreateTestPunch(At(0, 12), PunchKind.FixedDollar, _emp);

        var result = ShiftBuilder.BuildShifts([pair], [bonus], ctx);

        Assert.Single(result);
        Assert.Single(result[0].FixedEntries);
        Assert.Equal(PunchKind.FixedDollar, result[0].FixedEntries[0].Kind);
    }

    [Fact]
    public void FixedEntry_BetweenTwoShifts_AttachesToNearer()
    {
        // Two shifts: 9-13 and 20-22 (7-hr gap > default 6-hr threshold => two shifts)
        var ctx = TestEntityCreator.CreateContext();
        var pair1 = MakePair(At(0, 9),  At(0, 13));
        var pair2 = MakePair(At(0, 20), At(0, 22));

        // 15:00 — 2 hrs after shift1 ends vs 5 hrs before shift2 starts → shift1 (index 0)
        var closerToFirst = TestEntityCreator.CreateTestPunch(At(0, 15), PunchKind.FixedDollar, _emp);
        var result1 = ShiftBuilder.BuildShifts([pair1, pair2], [closerToFirst], ctx);
        Assert.Single(result1[0].FixedEntries);
        Assert.Empty(result1[1].FixedEntries);

        // 19:00 — 6 hrs after shift1 ends vs 1 hr before shift2 starts → shift2 (index 1)
        var closerToSecond = TestEntityCreator.CreateTestPunch(At(0, 19), PunchKind.FixedDollar, _emp);
        var result2 = ShiftBuilder.BuildShifts([pair1, pair2], [closerToSecond], ctx);
        Assert.Empty(result2[0].FixedEntries);
        Assert.Single(result2[1].FixedEntries);
    }

    [Fact]
    public void FixedEntry_InsideAShift_AttachesToThatShift_EvenWithOtherShiftsPresent()
    {
        // Two shifts: 9-13 and 20-22. An entry at 11:00 falls inside shift1's own range and
        // must attach there regardless of shift2 existing elsewhere in the list.
        var ctx = TestEntityCreator.CreateContext();
        var pair1 = MakePair(At(0, 9),  At(0, 13));
        var pair2 = MakePair(At(0, 20), At(0, 22));
        var entry = TestEntityCreator.CreateTestPunch(At(0, 11), PunchKind.FixedDollar, _emp);

        var result = ShiftBuilder.BuildShifts([pair1, pair2], [entry], ctx);

        Assert.Single(result[0].FixedEntries);
        Assert.Empty(result[1].FixedEntries);
    }

    [Fact]
    public void FixedEntryWithNoShifts_CreatesStandaloneShift()
    {
        var ctx   = TestEntityCreator.CreateContext();
        var bonus = TestEntityCreator.CreateTestPunch(At(0, 12), PunchKind.FixedDollar, _emp);

        var result = ShiftBuilder.BuildShifts([], [bonus], ctx);

        Assert.Single(result);
        Assert.Empty(result[0].PunchPairs);
        Assert.Single(result[0].FixedEntries);
    }

    [Fact]
    public void OrphanOutPair_AmongCompletePairs_DoesNotCrash()
    {
        // Regression: BuildFromPairs used to unconditionally dereference pair.InPunch when
        // ordering/gapping, throwing a NullReferenceException for an orphan Out (Out with no In).
        // The orphan sits far enough from both real shifts to become its own (unpaid) shift.
        var ctx = TestEntityCreator.CreateContext();
        var pair1 = MakePair(At(0, 9), At(0, 13));
        var orphanOut = TestEntityCreator.CreateTestPunchPair(null,
            TestEntityCreator.CreateTestPunch(At(1, 3), PunchKind.Out, _emp));
        var pair2 = MakePair(At(2, 9), At(2, 13));

        var result = ShiftBuilder.BuildShifts([pair1, orphanOut, pair2], [], ctx);

        Assert.Equal(3, result.Count);
        Assert.True(result[1].PunchPairs[0].IsMissingPunch);
    }

    [Fact]
    public void GapSpanningRuleChange_UsesRuleAtGapStart()
    {
        // Gap: Out Jan 2 22:00 -> In Jan 3 03:00 (5 hrs), spanning a PayRule change at midnight
        // Jan 3. Rule A (active at the gap's start, Jan 2) has a wide 10-hr threshold; Rule B
        // (active at the gap's end, Jan 3) has a narrow 2-hr threshold. If ShiftBuilder used the
        // gap-END rule it would split into two shifts (5 > 2); using the gap-START rule keeps
        // them in one shift (5 <= 10).
        var ruleA = new PayRule { DistanceBetweenShiftsHours = 10 };
        var ruleB = new PayRule { DistanceBetweenShiftsHours = 2 };
        var assignments = new[]
        {
            new PayRuleAssignment(ruleA, new LocalDate(2000, 1, 1), new LocalDate(2023, 1, 2)),
            new PayRuleAssignment(ruleB, new LocalDate(2023, 1, 3)),
        };
        var ctx = new PipelineContext(_emp, assignments, []);

        var pair1 = MakePair(Instant.FromUtc(2023, 1, 2, 18, 0), Instant.FromUtc(2023, 1, 2, 22, 0));
        var pair2 = MakePair(Instant.FromUtc(2023, 1, 3, 3, 0),  Instant.FromUtc(2023, 1, 3, 7, 0));

        var result = ShiftBuilder.BuildShifts([pair1, pair2], [], ctx);

        Assert.Single(result);
        Assert.Equal(2, result[0].PunchPairs.Count);
    }
}
