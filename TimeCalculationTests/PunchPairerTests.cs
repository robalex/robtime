using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.ShiftBuilding;
using Xunit;

namespace TimeCalculationTests;

public class PunchPairerTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static Instant At(int dayOffset, int hour) => Instant.FromUtc(2023, 1, 2 + dayOffset, hour, 0);

    private Punch In(Instant t)    => TestEntityCreator.CreateTestPunch(t, PunchKind.In,  _emp);
    private Punch Out(Instant t)   => TestEntityCreator.CreateTestPunch(t, PunchKind.Out, _emp);
    private Punch Dollar(Instant t) => TestEntityCreator.CreateTestPunch(t, PunchKind.FixedDollar, _emp);

    [Fact]
    public void NoPunches_ReturnsEmpty()
    {
        var (pairs, fixedEntries) = PunchPairer.PairPunches([], TestEntityCreator.CreateContext());
        Assert.Empty(pairs);
        Assert.Empty(fixedEntries);
    }

    [Fact]
    public void InThenOut_ProducesOnePair()
    {
        var (pairs, _) = PunchPairer.PairPunches(
            [In(At(0, 9)), Out(At(0, 17))],
            TestEntityCreator.CreateContext());
        Assert.Single(pairs);
        Assert.False(pairs[0].IsMissingPunch);
        Assert.Equal(8m, pairs[0].TotalHours);
    }

    [Fact]
    public void LoneIn_ProducesIncompletePair()
    {
        var (pairs, _) = PunchPairer.PairPunches([In(At(0, 9))], TestEntityCreator.CreateContext());
        Assert.Single(pairs);
        Assert.True(pairs[0].IsMissingPunch);
        Assert.Equal(0m, pairs[0].TotalHours);
    }

    [Fact]
    public void OrphanOut_ProducesIncompletePair()
    {
        var (pairs, _) = PunchPairer.PairPunches([Out(At(0, 17))], TestEntityCreator.CreateContext());
        Assert.Single(pairs);
        Assert.True(pairs[0].IsMissingPunch);
        Assert.Equal(0m, pairs[0].TotalHours);
    }

    [Fact]
    public void TwoCompletePairs_ProducesTwoPairs()
    {
        var punches = new[] { In(At(0, 9)), Out(At(0, 17)), In(At(1, 9)), Out(At(1, 17)) };
        var (pairs, _) = PunchPairer.PairPunches(punches, TestEntityCreator.CreateContext());
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.False(p.IsMissingPunch));
    }

    [Fact]
    public void PairExceedingMaxShiftLength_InOrphaned_ProducesTwoPairs()
    {
        var rule = new PayRule { MaxShiftLengthHours = 15 };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 6,  0), PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 2,  0), PunchKind.Out, _emp);
        var (pairs, _) = PunchPairer.PairPunches([inP, outP], ctx);
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMissingPunch));
        Assert.Equal(inP, pairs[0].InPunch);
        Assert.Equal(outP, pairs[1].OutPunch);
    }

    [Fact]
    public void ConsecutiveOrphanOuts_ProduceTwoIncompletePairs()
    {
        // Two Out punches in a row with no preceding In — must not crash,
        // and neither punch should be dropped
        var punches = new[] { Out(At(0, 9)), Out(At(0, 10)) };
        var (pairs, _) = PunchPairer.PairPunches(punches, TestEntityCreator.CreateContext());

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMissingPunch));
        Assert.Equal(punches[0], pairs[0].OutPunch);
        Assert.Equal(punches[1], pairs[1].OutPunch);
    }

    [Fact]
    public void FixedDollarEntry_IsSeparatedFromPairs()
    {
        var punches = new[] { In(At(0, 9)), Dollar(At(0, 10)), Out(At(0, 17)) };
        var (pairs, fixedEntries) = PunchPairer.PairPunches(punches, TestEntityCreator.CreateContext());
        Assert.Single(pairs);
        Assert.Single(fixedEntries);
        Assert.Equal(PunchKind.FixedDollar, fixedEntries[0].Kind);
    }

    [Fact]
    public void PairsGetAppliedRule()
    {
        var rule = new PayRule { Id = 99 };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var (pairs, _) = PunchPairer.PairPunches([In(At(0, 9)), Out(At(0, 17))], ctx);
        Assert.Equal(99, pairs[0].AppliedRule?.Id);
    }

    [Fact]
    public void PairSpanningRuleBoundary_IsSplitWithCorrectRules()
    {
        var ruleA = new PayRule { Id = 1 };
        var ruleB = new PayRule { Id = 2 };
        var assignments = new[]
        {
            new PayRuleAssignment(ruleA, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 2)),
            new PayRuleAssignment(ruleB, new LocalDate(2023, 1, 3)),
        };
        var employee = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
        var ctx = new PipelineContext(employee, assignments, []);

        // 22:00 Jan 2 → 06:00 Jan 3 spans the midnight Jan 3 boundary
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 22, 0), PunchKind.In,  employee);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 6,  0), PunchKind.Out, employee);
        var (pairs, _) = PunchPairer.PairPunches([inP, outP], ctx);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsSplit));
        Assert.Equal(1, pairs[0].AppliedRule?.Id);   // Jan 2 segment → Rule A
        Assert.Equal(2, pairs[1].AppliedRule?.Id);   // Jan 3 segment → Rule B
        Assert.Equal(2m, pairs[0].TotalHours);        // 22:00 → midnight = 2 hrs
        Assert.Equal(6m, pairs[1].TotalHours);        // midnight → 06:00 = 6 hrs
    }

    [Fact]
    public void PairSpanningPositionBoundary_IsSplitEvenWithSinglePayRule()
    {
        // One PayRule for the whole period, but the employee's position changes
        // mid-shift — the pair must still split at the position boundary so
        // Stage 4 can attach the correct rate to each half.
        var rule = new PayRule { Id = 1 };
        var payRuleAssignments = new[] { new PayRuleAssignment(rule, new LocalDate(2023, 1, 1)) };

        var posA = new Position { Id = 1, BaseRate = 15m, Name = "Server" };
        var posB = new Position { Id = 2, BaseRate = 20m, Name = "Shift Lead" };
        var positionAssignments = new[]
        {
            new EmployeePositionAssignment(posA, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 2)),
            new EmployeePositionAssignment(posB, new LocalDate(2023, 1, 3)),
        };

        var employee = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
        var ctx = new PipelineContext(employee, payRuleAssignments, positionAssignments);

        // 22:00 Jan 2 → 06:00 Jan 3 spans the midnight Jan 3 position-change boundary
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 22, 0), PunchKind.In,  employee);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 6,  0), PunchKind.Out, employee);
        var (pairs, _) = PunchPairer.PairPunches([inP, outP], ctx);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsSplit));
        Assert.Equal(2m, pairs[0].TotalHours);   // 22:00 → midnight = 2 hrs
        Assert.Equal(6m, pairs[1].TotalHours);   // midnight → 06:00 = 6 hrs

        var enriched = PairPositionAndRateAttacher.AttachPositionAndRateToPunchPairs(pairs, ctx);
        Assert.Equal(posA, enriched[0].Position);
        Assert.Equal(15m, enriched[0].Rate);
        Assert.Equal(posB, enriched[1].Position);
        Assert.Equal(20m, enriched[1].Rate);
    }
}
