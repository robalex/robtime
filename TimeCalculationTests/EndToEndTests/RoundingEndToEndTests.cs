using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class RoundingEndToEndTests : EndToEndTests
{
    [Fact]
    public void NearestIntervalRounding_ChangesFinalPay()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { RoundingRule = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // In 9:07 -> rounds to 9:00; Out 17:08 -> rounds to 17:15
        var punches = new List<Punch> { In(emp, At(2, 9, 7)), Out(emp, At(2, 17, 8)) };

        var result = PayCalculator.Calculate(punches, ctx);

        // 8.25 hrs @ $20 = 165
        Assert.Equal(165m, result.GrossPay);
        Assert.Equal(8.25m, result.Workweeks[0].RegularHours);
    }
}
