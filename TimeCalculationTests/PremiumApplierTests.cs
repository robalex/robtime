using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PremiumApplierTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private Shift EightHourNoBreaks()
    {
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 9, 0), PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 17, 0), PunchKind.Out, _emp);
        return new Shift { ShiftDate = new LocalDate(2023, 1, 2), PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = 20m }] };
    }

    private static PipelineContext CtxWith(params string[] codes)
    {
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string>(codes) };
        return new PipelineContext(
            new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m },
            [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
    }

    [Fact]
    public void NoActiveCodes_ShiftUnchanged()
    {
        var ctx = TestEntityCreator.CreateContext(employee: _emp);
        var result = PremiumApplier.Execute([EightHourNoBreaks()], ctx, _ => 20m);
        Assert.Empty(result[0].Premiums);
    }

    [Fact]
    public void ActiveMealAndRest_BothViolationsAttached()
    {
        var ctx = CtxWith("CA_MEAL", "CA_REST");
        var result = PremiumApplier.Execute([EightHourNoBreaks()], ctx, _ => 20m);

        Assert.Equal(2, result[0].Premiums.Count);
        Assert.Contains(result[0].Premiums, p => p.Code == "CA_MEAL" && p.Amount == 20m);
        Assert.Contains(result[0].Premiums, p => p.Code == "CA_REST" && p.Amount == 20m);
    }

    [Fact]
    public void Overrides_WaiveMealButNotRest()
    {
        var ctx = CtxWith("CA_MEAL", "CA_REST");
        var result = PremiumApplier.Execute(
            [EightHourNoBreaks()], ctx,
            _ => 20m,
            _ => [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver]);

        var meal = result[0].Premiums.Single(p => p.Code == "CA_MEAL");
        var rest = result[0].Premiums.Single(p => p.Code == "CA_REST");
        Assert.True(meal.Waived);
        Assert.Equal(0m, meal.Amount);
        Assert.False(rest.Waived);      // CA rest is not waivable
        Assert.Equal(20m, rest.Amount);
    }
}
