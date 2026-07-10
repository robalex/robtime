using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class Stage2_InferPunchKindsTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static Instant At(int dayOffset, int hour) => Instant.FromUtc(2023, 1, 2 + dayOffset, hour, 0);

    private IReadOnlyList<Punch> Run(params Punch[] punches)
        => Stage2_InferPunchKinds.Execute(punches, TestEntityCreator.CreateContext());

    [Fact]
    public void FirstClock_BecomesIn()
    {
        var punch = TestEntityCreator.CreateTestPunch(At(0, 9), PunchKind.Clock, _emp);
        var result = Run(punch);
        Assert.Equal(PunchKind.In, result[0].Kind);
    }

    [Fact]
    public void ClockAfterOut_BecomesIn()
    {
        var p1 = TestEntityCreator.CreateTestPunch(At(0, 9),  PunchKind.Clock, _emp);
        var p2 = TestEntityCreator.CreateTestPunch(At(0, 17), PunchKind.Clock, _emp);
        var result = Run(p1, p2);
        Assert.Equal(PunchKind.In,  result[0].Kind);
        Assert.Equal(PunchKind.Out, result[1].Kind);

        var p3 = TestEntityCreator.CreateTestPunch(At(1, 9),  PunchKind.Clock, _emp);
        var result2 = Stage2_InferPunchKinds.Execute([p1, p2, p3], TestEntityCreator.CreateContext());
        Assert.Equal(PunchKind.In, result2[2].Kind);
    }

    [Fact]
    public void ClockAfterIn_WithinResetWindow_BecomesOut()
    {
        var p1 = TestEntityCreator.CreateTestPunch(At(0, 9),  PunchKind.Clock, _emp);
        var p2 = TestEntityCreator.CreateTestPunch(At(0, 17), PunchKind.Clock, _emp);
        var result = Run(p1, p2);
        Assert.Equal(PunchKind.Out, result[1].Kind);
    }

    [Fact]
    public void ClockAfterIn_BeyondResetWindow_BecomesNewIn()
    {
        // Gap = 20 hrs > default 15 hr reset window
        var rule = new PayRule { PunchPairResetHours = 15 };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var p1 = TestEntityCreator.CreateTestPunch(At(0, 9),  PunchKind.Clock, _emp);
        var p2 = TestEntityCreator.CreateTestPunch(At(1, 5),  PunchKind.Clock, _emp);  // 20 hrs later
        var result = Stage2_InferPunchKinds.Execute([p1, p2], ctx);
        Assert.Equal(PunchKind.In, result[0].Kind);
        Assert.Equal(PunchKind.In, result[1].Kind);  // orphan reset
    }

    [Fact]
    public void AlreadyResolvedInOut_PassThroughUnchanged()
    {
        var p1 = TestEntityCreator.CreateTestPunch(At(0, 9),  PunchKind.In,  _emp);
        var p2 = TestEntityCreator.CreateTestPunch(At(0, 17), PunchKind.Out, _emp);
        var result = Run(p1, p2);
        Assert.Equal(PunchKind.In,  result[0].Kind);
        Assert.Equal(PunchKind.Out, result[1].Kind);
    }

    [Fact]
    public void FixedDollarPunch_PassesThroughUnchanged()
    {
        var punch = TestEntityCreator.CreateTestPunch(At(0, 9), PunchKind.FixedDollar, _emp);
        var result = Run(punch);
        Assert.Equal(PunchKind.FixedDollar, result[0].Kind);
    }

    [Fact]
    public void FixedHoursPunch_PassesThroughUnchanged()
    {
        var punch = TestEntityCreator.CreateTestPunch(At(0, 9), PunchKind.FixedHours, _emp);
        var result = Run(punch);
        Assert.Equal(PunchKind.FixedHours, result[0].Kind);
    }

    [Fact]
    public void ConfigurableResetWindow_IsRespected()
    {
        var rule = new PayRule { PunchPairResetHours = 5 };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var p1 = TestEntityCreator.CreateTestPunch(At(0, 9), PunchKind.Clock, _emp);
        // 6 hours later — beyond the 5-hr custom reset window
        var p2 = TestEntityCreator.CreateTestPunch(At(0, 15), PunchKind.Clock, _emp);
        var result = Stage2_InferPunchKinds.Execute([p1, p2], ctx);
        Assert.Equal(PunchKind.In, result[1].Kind);
    }
}
