
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
    public void Differential_FeedsRegularRate_BeforeOvertimePremiumIsPriced()
    {
        // 5 days x 9 hrs @ $20 = 45 hrs (federal OT: 40 regular + 5 overtime). A flat $2/hr
        // "HAZARD" differential applies to every hour worked, all week. The differential must
        // raise the regular rate BEFORE the overtime premium is priced from it — this is the
        // thing that would silently go wrong if OT were computed from the plain hourly rate
        // instead of the weighted regular rate (which includes differentials per FLSA 29 CFR
        // 778): the premium would come out as 5 x 0.5 x $20 = $50 instead of the correct
        // 5 x 0.5 x $22 = $55.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var hazard = new DifferentialRule
        {
            Code = "HAZARD",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
            // WindowStart == WindowEnd (both left default) => IsAllDay: applies to every hour worked.
        };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [hazard]);

        var punches = new List<Punch>();
        for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 8))); punches.Add(Out(emp, At(d, 17))); }

        var result = PayCalculator.Calculate(punches, ctx);
        var week = result.Workweeks[0];

        // straight 45x20=900; differential 45x2=90; RROP=(900+90)/45=22 (not 20)
        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(5m, week.OvertimeHours);
        Assert.Equal(22m, week.RegularRate);

        // OT premium priced at the differential-inflated rate: 5x0.5x22=55, not 5x0.5x20=50
        var otLine = result.LineItems.Single(l => l.Code == "OVERTIME");
        Assert.Equal(55m, otLine.Amount);
        Assert.Equal(22m, otLine.BaseRate);
        Assert.Equal(0.5m, otLine.Multiplier);
        Assert.NotEqual(50m, otLine.Amount);   // what it would be if the differential were ignored

        // The differential itself is unaffected by overtime -> still exactly 45 x $2, split one
        // line per shift (5 shifts, one per day).
        var diffLines = result.LineItems.Where(l => l.Code == "HAZARD").ToList();
        Assert.Equal(5, diffLines.Count);
        Assert.Equal(90m, diffLines.Sum(l => l.Amount));

        // Everything reconciles: straight 900 + differential 90 + OT premium 55 = 1045
        Assert.Equal(1045m, result.GrossPay);
    }

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
