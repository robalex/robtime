using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class DstEndToEndTests : EndToEndTests
{
    [Fact]
    public void FallBackTransition_ExtraHour_PaidCorrectly_EndToEnd()
    {
        // 11 PM Nov 4 EDT -> 3 AM Nov 5 EST: the repeated 1 AM hour means 5 real hours elapsed,
        // not 4. Runs through the FULL pipeline (rounding, subtyping, pairing, dating, RROP,
        // overtime, summarizing) in the employee's real timezone, not just the punch-pairing stage.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "America/New_York", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 3, 0), PunchKind.In, emp),
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 8, 0), PunchKind.Out, emp),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(100m, result.GrossPay);   // 5 real hrs @ $20
        Assert.Equal(5m, result.Workweeks[0].RegularHours);
        Assert.Equal(new LocalDate(2023, 11, 4), result.Workweeks[0].Shifts.Single().ShiftDate);
    }
}
