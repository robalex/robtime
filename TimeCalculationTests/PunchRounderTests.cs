using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PunchRounderTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private static Instant At(int hour, int minute) => Instant.FromUtc(2023, 1, 2, hour, minute);
    private static Instant At(int hour, int minute, int second) => Instant.FromUtc(2023, 1, 2, hour, minute, second);

    [Fact]
    public void NoRounding_LeavesTimeUnchanged()
    {
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.None };
        var ctx  = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 3), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Null(result[0].RoundedPunchTime);
        Assert.Equal(At(9, 3), result[0].EffectiveTime);
    }

    [Fact]
    public void NearestInterval_RoundsDownToQuarterHour()
    {
        // 9:06 → nearest 15 min → 9:00
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var ctx  = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 6), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 0), result[0].EffectiveTime);
    }

    [Fact]
    public void NearestInterval_RoundsUpToQuarterHour()
    {
        // 9:09 → nearest 15 min → 9:15
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var ctx  = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 9), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 15), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_WithinGraceBeforeHour_RoundsBack()
    {
        // 9:03 — within 7-min grace of :00 → rounds to 9:00
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 3), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 0), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_WithinGraceBeforeBoundary_RoundsForward()
    {
        // 9:13 — within 7-min grace of :15 → rounds to 9:15
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 13), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 15), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_OutsideGrace_LeavesTimeUnchanged()
    {
        // With 5-min grace and 15-min interval: 9:09 is 9 min from :00 and 6 min from :15 — outside both windows
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 5,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 9), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 9), result[0].EffectiveTime);
        Assert.Null(result[0].RoundedPunchTime);
    }

    [Fact]
    public void NearestInterval_SecondsFlipRoundingDecision()
    {
        // 3:42:59 — without seconds this would round down to 3:40; seconds push it past midpoint to 3:45
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 5 };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(15, 42, 59), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(15, 45), result[0].EffectiveTime);
    }

    [Fact]
    public void NearestInterval_SecondsJustBeforeMidpoint_RoundsDown()
    {
        // 9:07:29 — 449 s past 9:00, 451 s before 9:15 → rounds to 9:00
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 7, 29), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 0), result[0].EffectiveTime);
    }

    [Fact]
    public void NearestInterval_SecondsJustAfterMidpoint_RoundsUp()
    {
        // 9:07:31 — 451 s past 9:00, 449 s before 9:15 → rounds to 9:15
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 7, 31), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 15), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_ExactlyAtGraceBoundary_RoundsBack()
    {
        // 9:07:00 — exactly 420 s (7 min) past 9:00; at the boundary, grace applies → rounds to 9:00
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 7, 0), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 0), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_OneSecondPastBackGrace_LeavesUnchanged()
    {
        // 9:07:01 — 421 s past 9:00, 479 s before 9:15; outside both grace windows → unchanged
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 7, 1), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 7, 1), result[0].EffectiveTime);
        Assert.Null(result[0].RoundedPunchTime);
    }

    [Fact]
    public void QuarterHourWithGrace_ExactlyAtForwardGraceBoundary_RoundsForward()
    {
        // 9:08:00 — 420 s (7 min) before 9:15; at the boundary, grace applies → rounds to 9:15
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 8, 0), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 15), result[0].EffectiveTime);
    }

    [Fact]
    public void QuarterHourWithGrace_OneSecondPastForwardGrace_LeavesUnchanged()
    {
        // 9:07:59 — 421 s before 9:15, 479 s past 9:00; outside both grace windows → unchanged
        var rule = new PayRule();
        var rounding = new RoundingRule
        {
            RoundingStrategy = RoundingStrategy.QuarterHourWithGrace,
            RoundingIntervalMinutes = 15,
            RoundingGraceMinutes = 7,
        };
        var ctx   = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 7, 59), PunchKind.In, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 7, 59), result[0].EffectiveTime);
        Assert.Null(result[0].RoundedPunchTime);
    }

    [Fact]
    public void FixedDollarPunch_IsAlsoRounded()
    {
        var rule = new PayRule();
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var ctx  = TestEntityCreator.CreateContext(rule, null, rounding);
        var punch = TestEntityCreator.CreateTestPunch(At(9, 6), PunchKind.FixedDollar, _emp);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(At(9, 0), result[0].EffectiveTime);
    }
}
