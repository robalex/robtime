using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class Stage4_EnrichPairsTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static Instant At(int hour) => Instant.FromUtc(2023, 1, 2, hour, 0);

    private PunchPair MakePair(int? positionId = null)
    {
        var inP  = TestEntityCreator.CreateTestPunch(At(9),  PunchKind.In,  _emp) with { PositionId = positionId };
        var outP = TestEntityCreator.CreateTestPunch(At(17), PunchKind.Out, _emp);
        return TestEntityCreator.CreateTestPunchPair(inP, outP);
    }

    [Fact]
    public void NoPositionAssignment_UsesEmployeeMinimumWage()
    {
        var ctx  = TestEntityCreator.CreateContext(employee: _emp);
        var pair = MakePair();

        var result = Stage4_EnrichPairs.Execute([pair], ctx);

        Assert.Null(result[0].Position);
        Assert.Equal(15m, result[0].Rate);
    }

    [Fact]
    public void ActivePositionAssignment_AttachesPositionAndRate()
    {
        var position = new Position { Id = 10, BaseRate = 20m, Name = "Cook" };
        var assignments = new[] { new EmployeePositionAssignment(position, new LocalDate(2023, 1, 1)) };
        var ctx = TestEntityCreator.CreateContext(employee: _emp, positions: assignments);
        var pair = MakePair();

        var result = Stage4_EnrichPairs.Execute([pair], ctx);

        Assert.Equal(position, result[0].Position);
        Assert.Equal(20m, result[0].Rate);
    }

    [Fact]
    public void PunchPositionIdOverride_UsesOverridePosition()
    {
        var defaultPos = new Position { Id = 1, BaseRate = 15m, Name = "Server" };
        var overridePos = new Position { Id = 2, BaseRate = 20m, Name = "Bartender" };
        var assignments = new[]
        {
            new EmployeePositionAssignment(defaultPos, new LocalDate(2023, 1, 1)),
            new EmployeePositionAssignment(overridePos, new LocalDate(2023, 1, 1)),
        };
        var ctx  = TestEntityCreator.CreateContext(employee: _emp, positions: assignments);
        var pair = MakePair(positionId: 2);   // punch specifies position 2

        var result = Stage4_EnrichPairs.Execute([pair], ctx);

        Assert.Equal(overridePos, result[0].Position);
        Assert.Equal(20m, result[0].Rate);
    }
}
