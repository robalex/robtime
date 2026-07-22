using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class StatePremiumEndToEndTests : EndToEndTests
{
    [Fact]
    public void CaMeal_ViolationWaivedByBothOverrides_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h, no lunch

        var withoutOverride = PayCalculator.Calculate(punches, ctx);
        var withOverride = PayCalculator.Calculate(punches, ctx,
            overridesForShift: _ => [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver]);

        Assert.Equal(180m, withoutOverride.GrossPay);   // 160 straight + 20 premium
        Assert.Equal(160m, withOverride.GrossPay);       // waived
    }

    [Fact]
    public void CaRest_NotWaivable_ChargedEvenWithOverrides_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_REST" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h, no breaks

        var result = PayCalculator.Calculate(punches, ctx,
            overridesForShift: _ => [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver]);

        // 8h requires 2 rests (RestSchedule.Required(8)=2), 0 taken -> violated regardless of overrides
        Assert.Equal(180m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 20m);
    }

    [Fact]
    public void CaMeal_PerWorkdayCap_AcrossGenuinelySplitShifts_EndToEnd()
    {
        // Two shifts on the same calendar day with a 7-hr gap between them (> the default 6-hr
        // DistanceBetweenShiftsHours), so ShiftBuilder genuinely splits them. Both qualify for a
        // meal violation on their own (>5h, no lunch), but only one premium is owed per workday.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            In(emp, At(2, 6)),  Out(emp, At(2, 14)),   // morning: 8h
            In(emp, At(2, 21)), Out(emp, At(3, 3)),    // evening: 6h (7-hr gap from 14:00 to 21:00)
        };

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 14h×20=280; only ONE $20 premium for the day (not two) → 300
        Assert.Equal(300m, result.GrossPay);
        Assert.Single(result.LineItems, l => l.Type == PayLineType.Premium);
    }

    [Fact]
    public void PrMeal_PaidAtOvertimeRate_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "PR_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 16)) };   // 7h, no meal

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 7×20=140; premium=1×(20×1.5)=30 → 170
        Assert.Equal(170m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 30m);
    }

    [Fact]
    public void WaMeal_HalfHourRemedy_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "WA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 15)) };   // 6h, no meal

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 6×20=120; premium=0.5×20=10 → 130
        Assert.Equal(130m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 10m);
    }

    [Fact]
    public void OrMeal_SixHourThreshold_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "OR_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);
        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 15)) };   // exactly 6h, no meal

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 6×20=120; premium=1×20=20 → 140
        Assert.Equal(140m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 20m);
    }
}
