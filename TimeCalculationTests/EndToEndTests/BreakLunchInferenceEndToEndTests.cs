using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class BreakLunchInferenceEndToEndTests : EndToEndTests
{
    [Fact]
    public void AutoClassifiedLunch_SatisfiesCaMeal_NoPremiumCharged()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // 3h, then a 30-min gap (matches ExpectedLunchLengthMinutes exactly -> auto-classified
        // Lunch, not Break), then 4.5h. Lunch begins at worked-hour 3, well inside the 5-hour window.
        var punches = new List<Punch>
        {
            In(emp, At(2, 9)), Out(emp, At(2, 12)),
            In(emp, At(2, 12, 30)), Out(emp, At(2, 17)),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(150m, result.GrossPay);   // 7.5h @ $20, no premium
        Assert.DoesNotContain(result.LineItems, l => l.Type == PayLineType.Premium);
    }

    [Fact]
    public void ShortGap_AutoClassifiedAsBreakNotLunch_CaMealViolated()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // Same total shape, but the gap is 15 min (matches ExpectedBreakLengthMinutes) ->
        // auto-classified as Break, not Lunch, so it can't satisfy the meal requirement.
        var punches = new List<Punch>
        {
            In(emp, At(2, 9)), Out(emp, At(2, 12)),
            In(emp, At(2, 12, 15)), Out(emp, At(2, 17)),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 7.75×20=155; RROP=20; premium=1×20=20 → 175
        Assert.Equal(175m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 20m);
    }
}
