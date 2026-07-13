using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PipelineContextTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    [Fact]
    public void GetBoundaryInstantsBetween_SinglePayRule_MultiplePositions_StillReportsPositionBoundary()
    {
        // Regression: the method used to bail out early whenever there was only one
        // PayRuleAssignment, silently ignoring position boundaries entirely.
        var rule = new PayRule { Id = 1 };
        var payRules = new[] { new PayRuleAssignment(rule, new LocalDate(2023, 1, 1)) };

        var posA = new Position { Id = 1 };
        var posB = new Position { Id = 2 };
        var positions = new[]
        {
            new EmployeePositionAssignment(posA, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 2)),
            new EmployeePositionAssignment(posB, new LocalDate(2023, 1, 3)),
        };

        var ctx = new PipelineContext(_emp, payRules, positions);
        var boundaries = ctx.GetBoundaryInstantsBetween(
            Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        Assert.Single(boundaries);
        Assert.Equal(Instant.FromUtc(2023, 1, 3, 0, 0), boundaries[0]);
    }

    [Fact]
    public void GetBoundaryInstantsBetween_RuleAndPositionChangeSameDay_ReportsOneBoundary()
    {
        var ruleA = new PayRule { Id = 1 };
        var ruleB = new PayRule { Id = 2 };
        var payRules = new[]
        {
            new PayRuleAssignment(ruleA, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 2)),
            new PayRuleAssignment(ruleB, new LocalDate(2023, 1, 3)),
        };

        var posA = new Position { Id = 1 };
        var posB = new Position { Id = 2 };
        var positions = new[]
        {
            new EmployeePositionAssignment(posA, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 2)),
            new EmployeePositionAssignment(posB, new LocalDate(2023, 1, 3)),
        };

        var ctx = new PipelineContext(_emp, payRules, positions);
        var boundaries = ctx.GetBoundaryInstantsBetween(
            Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        Assert.Single(boundaries);
    }

    [Fact]
    public void GetBoundaryInstantsBetween_NoAssignmentChanges_ReturnsEmpty()
    {
        var rule = new PayRule { Id = 1 };
        var ctx = new PipelineContext(_emp, [new PayRuleAssignment(rule, new LocalDate(2023, 1, 1))], []);

        var boundaries = ctx.GetBoundaryInstantsBetween(
            Instant.FromUtc(2023, 1, 2, 9, 0), Instant.FromUtc(2023, 1, 2, 17, 0));

        Assert.Empty(boundaries);
    }

    [Fact]
    public void TryGetRuleAt_OutsideCoverage_ReturnsFalse()
    {
        var rule = new PayRule { Id = 1 };
        var ctx = new PipelineContext(_emp,
            [new PayRuleAssignment(rule, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 31))], []);

        var found = ctx.TryGetRuleAt(Instant.FromUtc(2023, 3, 1, 0, 0), out var resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void TryGetRuleAt_InsideCoverage_ReturnsTrueAndRule()
    {
        var rule = new PayRule { Id = 7 };
        var ctx = new PipelineContext(_emp, [new PayRuleAssignment(rule, new LocalDate(2023, 1, 1))], []);

        var found = ctx.TryGetRuleAt(Instant.FromUtc(2023, 6, 1, 0, 0), out var resolved);

        Assert.True(found);
        Assert.Equal(7, resolved.Id);
    }

    [Fact]
    public void GetRuleAt_OutsideCoverage_Throws()
    {
        var ctx = new PipelineContext(_emp, [], []);

        Assert.Throws<InvalidOperationException>(() => ctx.GetRuleAt(Instant.FromUtc(2023, 1, 1, 0, 0)));
    }

    [Fact]
    public void GetRuleAt_OverlappingAssignments_LatestEffectiveFromWins()
    {
        // Pins the lookup semantics before the linear scan is replaced with a binary search:
        // when two assignments both cover a date, the one with the latest EffectiveFrom wins.
        var early = new PayRule { Id = 1 };
        var late = new PayRule { Id = 2 };
        var assignments = new[]
        {
            new PayRuleAssignment(early, new LocalDate(2023, 1, 1), new LocalDate(2023, 6, 1)),
            new PayRuleAssignment(late, new LocalDate(2023, 3, 1), new LocalDate(2023, 12, 31)),
        };
        var ctx = new PipelineContext(_emp, assignments, []);

        var rule = ctx.GetRuleAt(Instant.FromUtc(2023, 4, 1, 0, 0));

        Assert.Equal(2, rule.Id);
    }

    [Fact]
    public void GetRuleAt_DuplicateEffectiveFromDates_LastSuppliedOfTheTieWins()
    {
        // Two assignments share the exact same EffectiveFrom (a degenerate but possible input).
        // The binary search can land on either one of the tied pair; the tie-break forward scan
        // must resolve to the last one in sort-stable order, same as a full linear scan would.
        var first = new PayRule { Id = 1 };
        var second = new PayRule { Id = 2 };
        var assignments = new[]
        {
            new PayRuleAssignment(first, new LocalDate(2023, 1, 1)),
            new PayRuleAssignment(second, new LocalDate(2023, 1, 1)),
        };
        var ctx = new PipelineContext(_emp, assignments, []);

        var rule = ctx.GetRuleAt(Instant.FromUtc(2023, 6, 1, 0, 0));

        Assert.Equal(2, rule.Id);
    }

    [Fact]
    public void GetRuleAt_LatestAssignmentDoesNotCover_FallsBackToEarlierCoveringOne()
    {
        // The latest-starting assignment (EffectiveFrom Mar 1) doesn't cover the queried date
        // (its own EffectiveTo ends it in April); an earlier, still-covering assignment must win.
        var earlyOpenEnded = new PayRule { Id = 1 };
        var laterButExpired = new PayRule { Id = 2 };
        var assignments = new[]
        {
            new PayRuleAssignment(earlyOpenEnded, new LocalDate(2023, 1, 1)),
            new PayRuleAssignment(laterButExpired, new LocalDate(2023, 3, 1), new LocalDate(2023, 4, 1)),
        };
        var ctx = new PipelineContext(_emp, assignments, []);

        var rule = ctx.GetRuleAt(Instant.FromUtc(2023, 6, 1, 0, 0));

        Assert.Equal(1, rule.Id);
    }

    [Fact]
    public void GetPositionAt_OverlappingAssignments_LatestEffectiveFromWins()
    {
        var early = new Position { Id = 1 };
        var late = new Position { Id = 2 };
        var assignments = new[]
        {
            new EmployeePositionAssignment(early, new LocalDate(2023, 1, 1), new LocalDate(2023, 6, 1)),
            new EmployeePositionAssignment(late, new LocalDate(2023, 3, 1), new LocalDate(2023, 12, 31)),
        };
        var ctx = new PipelineContext(_emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], assignments);

        var position = ctx.GetPositionAt(Instant.FromUtc(2023, 4, 1, 0, 0));

        Assert.Equal(2, position?.Id);
    }
}
