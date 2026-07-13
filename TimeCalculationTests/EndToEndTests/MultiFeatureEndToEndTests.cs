
using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class MultiFeatureEndToEndTests : EndToEndTests
{
    [Fact]
    public void ComplexShift_DailyOvertime_CompliantMeal_ViolatedRest_AllReconcile()
    {
        // One 8.5-hr shift (excluding an auto-classified 30-min lunch): daily OT splits it into
        // 8 regular + 0.5 OT; the lunch satisfies CA_MEAL (no charge); no rest breaks were taken,
        // so CA_REST is violated (not waivable). Proves subtype inference, daily overtime,
        // regular-rate calculation, and two different premium outcomes all cooperate correctly
        // from raw punches through to the final itemized pay.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule
        {
            OvertimeRule = new OvertimeRule { HasDailyOvertime = true },
            ActivePremiumCodes = new HashSet<string> { "CA_MEAL", "CA_REST" },
        };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            In(emp, At(2, 6)),  Out(emp, At(2, 10)),         // 4h
            In(emp, At(2, 10, 30)), Out(emp, At(2, 15)),     // 4.5h (30-min lunch in between)
        };

        var result = PayCalculator.Calculate(punches, ctx);
        var shift = result.Workweeks[0].Shifts.Single();

        // straight 8.5×20=170 (across 2 Regular lines: 80 + 90); OT premium 0.5×0.5×20=5;
        // CA_REST premium 1×20=20 (2 rests required for 8.5h, 0 taken); no CA_MEAL charge → 195
        Assert.Equal(195m, result.GrossPay);
        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(0.5m, result.Workweeks[0].OvertimeHours);

        var regularLines = shift.LineItems.Where(l => l.Type == PayLineType.Regular).ToList();
        Assert.Equal(2, regularLines.Count);
        Assert.Equal(170m, regularLines.Sum(l => l.Amount));

        Assert.Contains(shift.LineItems, l => l.Type == PayLineType.OvertimePremium && l.Amount == 5m);
        Assert.Contains(shift.LineItems, l => l.Type == PayLineType.Premium && l.Description.Contains("CA_REST") && l.Amount == 20m);
        Assert.DoesNotContain(shift.LineItems, l => l.Description.Contains("CA_MEAL"));
    }
}
