using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class PairingAndOrphanEndToEndTests : EndToEndTests
{
    [Fact]
    public void FullWeek_StraightTime_NoOvertime()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 18m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 9))); punches.Add(Out(emp, At(d, 16))); }

        var result = PayCalculator.Calculate(punches, ctx);

        // 5 days × 7 hrs × $18 = 630, no OT
        Assert.Equal(630m, result.GrossPay);
        Assert.Equal(35m, result.Workweeks[0].RegularHours);
        Assert.Equal(0m, result.Workweeks[0].OvertimeHours);
        // Validate that LineItems are anchored to the correct punch
        Assert.Equal(1, result.LineItems[0].AnchorPunchId);
        Assert.Equal(3, result.LineItems[1].AnchorPunchId);
        Assert.Equal(5, result.LineItems[2].AnchorPunchId);
        Assert.Equal(7, result.LineItems[3].AnchorPunchId);
        Assert.Equal(9, result.LineItems[4].AnchorPunchId);
    }

    [Fact]
    public void OrphanInPunch_AmongCompleteDays_DoesNotCrash_AndContributesNoPay()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            In(emp, At(2, 9)), Out(emp, At(2, 17)),  // Mon: complete, 8h @ $20 = 160
            In(emp, At(3, 9)),                        // Tue: lone In, never clocked out
            In(emp, At(4, 9)), Out(emp, At(4, 17)),  // Wed: complete, 8h @ $20 = 160
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(320m, result.GrossPay);
        Assert.Equal(2, result.Workweeks[0].Shifts.Count);   // the orphan produces no pay lines
    }

    [Fact]
    public void OrphanOutPunch_AmongCompleteDays_DoesNotCrash_AndContributesNoPay()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            Out(emp, At(2, 9)),                       // Mon: a lone Out, no preceding In
            In(emp, At(4, 9)), Out(emp, At(4, 17)),  // Wed: complete, 8h @ $20 = 160
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(160m, result.GrossPay);
        Assert.Single(result.Workweeks[0].Shifts);
    }
}
