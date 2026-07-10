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
}
