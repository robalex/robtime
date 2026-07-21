using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

/// <summary>
/// End-to-end confidence suite: every test starts from raw Punch objects and runs the whole
/// pipeline via PayCalculator.Calculate (or, for retroactive bonus, the real Workweek objects the
/// pipeline produces), asserting hand-computed expected pay. Grouped by feature area so the file
/// reads as a map of what's been proven to work together, not just in isolation.
///
/// This suite is what found (and this file's fixes proved) three real crash bugs: PairPositionAndRateAttacher,
/// ShiftBuilder, and ShiftDater all used to unconditionally dereference PunchPair.InPunch, which
/// is null for an orphan Out (an Out punch with no preceding In). Every existing unit test for
/// those stages happened to pass a pair with InPunch set, so the bug only surfaced once stages
/// were chained together — exactly what end-to-end testing is for.
/// </summary>
public abstract class EndToEndTests
{
    protected static Instant At(int day, int hour, int minute = 0) => Instant.FromUtc(2023, 1, day, hour, minute);

    // Instance field, not static: xunit creates a fresh EndToEndTests instance per [Fact], but a
    // static field would persist across the whole test run, so the counter (and thus every
    // AnchorPunchId assertion below) would depend on what order tests happened to execute in.
    protected int _currentPunchId = 1;

    protected Punch In(Employee emp, Instant t, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.In, emp, _currentPunchId++) with { PositionId = positionId };
    protected Punch Out(Employee emp, Instant t, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.Out, emp, _currentPunchId++) with { PositionId = positionId };
}
