using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
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
    public void GapSpanningRuleChange_UsesRuleAtGapStart()
    {
        // Gap: Out Jan 2 22:00 -> In Jan 3 03:00 (5 hrs), spanning a PayRule change at midnight
        // Jan 3. Rule A (active at the gap's start, Jan 2) has a wide 10-hr threshold; Rule B
        // (active at the gap's end, Jan 3) has a narrow 2-hr threshold. If ShiftBuilder used the
        // gap-END rule it would split into two shifts (5 > 2); using the gap-START rule (as
        // PunchSubtypeInferrer also does) keeps them in one shift (5 <= 10).
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
