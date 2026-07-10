using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class Stage5_BuildShiftsTests
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
        var result = Stage5_BuildShifts.Execute([], [], ctx);
        Assert.Empty(result);
    }

    [Fact]
    public void SinglePair_ProducesOneShift()
    {
        var ctx    = TestEntityCreator.CreateContext();
        var pair   = MakePair(At(0, 9), At(0, 17));
        var result = Stage5_BuildShifts.Execute([pair], [], ctx);
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
        var result = Stage5_BuildShifts.Execute([pair1, pair2], [], ctx);
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
        var result = Stage5_BuildShifts.Execute([pair1, pair2], [], ctx);
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
        var result = Stage5_BuildShifts.Execute([pair1, pair2], [], ctx);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FixedEntry_AttachedToNearestShift()
    {
        var ctx = TestEntityCreator.CreateContext();
        var pair = MakePair(At(0, 9), At(0, 17));
        var bonus = TestEntityCreator.CreateTestPunch(At(0, 12), PunchKind.FixedDollar, _emp);

        var result = Stage5_BuildShifts.Execute([pair], [bonus], ctx);

        Assert.Single(result);
        Assert.Single(result[0].FixedEntries);
        Assert.Equal(PunchKind.FixedDollar, result[0].FixedEntries[0].Kind);
    }

    [Fact]
    public void FixedEntryWithNoShifts_CreatesStandaloneShift()
    {
        var ctx   = TestEntityCreator.CreateContext();
        var bonus = TestEntityCreator.CreateTestPunch(At(0, 12), PunchKind.FixedDollar, _emp);

        var result = Stage5_BuildShifts.Execute([], [bonus], ctx);

        Assert.Single(result);
        Assert.Empty(result[0].PunchPairs);
        Assert.Single(result[0].FixedEntries);
    }
}
