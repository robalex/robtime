using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class PayCalculatorTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };

    // Position paying $20 so straight-time math is round.
    private static PipelineContext Context(PayRule? rule = null)
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var pos = new Position { Id = 5, BaseRate = 20m, Name = "Line" };
        return new PipelineContext(
            emp,
            [new PayRuleAssignment(rule ?? new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(pos, new LocalDate(2000, 1, 1))]);
    }

    // Builds In/Out punches for `days` consecutive days starting Mon Jan 2 2023, each `hours` long from 09:00.
    private List<Punch> Week(int days, int hours)
    {
        var punches = new List<Punch>();
        for (int d = 0; d < days; d++)
        {
            var start = Instant.FromUtc(2023, 1, 2 + d, 9, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(hours), PunchKind.Out, _emp));
        }
        return punches;
    }

    [Fact]
    public void FortyHourWeek_NoOvertime_GrossIsStraightTime()
    {
        var result = PayCalculator.Calculate(Week(5, 8), Context());

        Assert.Single(result.Workweeks);
        Assert.Equal(800m, result.GrossPay);                 // 40 × $20
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(0m, result.Workweeks[0].OvertimeHours);
        // Regular is now itemized per punch pair (one per shift here), not one aggregate line
        Assert.Equal(800m, result.LineItems.Where(l => l.Type == PayLineType.Regular).Sum(l => l.Amount));
        Assert.DoesNotContain(result.LineItems, l => l.Type == PayLineType.OvertimePremium);
    }

    [Fact]
    public void FiftyHourWeek_Federal_TenHoursOvertimePremium()
    {
        var result = PayCalculator.Calculate(Week(5, 10), Context());

        // straight 50 × 20 = 1000, OT premium 10 × 0.5 × 20 = 100 → 1100
        Assert.Equal(1100m, result.GrossPay);
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(10m, result.Workweeks[0].OvertimeHours);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.OvertimePremium && l.Amount == 100m);
    }

    [Fact]
    public void California_TwelveHourDay_ProducesDailyOvertime()
    {
        var rule = new PayRule();
        rule.OvertimeRule.HasDailyOvertime = true;
        // one 12-hour day: 8 reg + 4 OT
        var result = PayCalculator.Calculate(Week(1, 12), Context(rule));

        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(4m, result.Workweeks[0].OvertimeHours);
        // straight 12 × 20 = 240, OT premium 4 × 0.5 × 20 = 40 → 280
        Assert.Equal(280m, result.GrossPay);
    }

    [Fact]
    public void Idempotent_SameInputsProduceEqualLineItems()
    {
        var a = PayCalculator.Calculate(Week(5, 10), Context());
        var b = PayCalculator.Calculate(Week(5, 10), Context());

        Assert.Equal(a.GrossPay, b.GrossPay);
        Assert.Equal(a.LineItems, b.LineItems);   // PayLineItem is a scalar record → structural equality
    }

    [Fact]
    public void EmptyPunches_ProduceEmptyResult()
    {
        var result = PayCalculator.Calculate([], Context());
        Assert.Empty(result.Workweeks);
        Assert.Equal(0m, result.GrossPay);
    }

    [Fact]
    public void OvertimeBoundary_FallingMidPair_SplitsRegularAndPremiumWithinThatPair()
    {
        // 4 days @ 8 hrs (32) + 1 day @ 12 hrs (44 total): the 40-hr federal threshold falls
        // inside day 5's single pair (8 hrs regular + 4 hrs overtime), not at a shift boundary.
        var punches = new List<Punch>();
        for (int d = 0; d < 4; d++)
        {
            var start = Instant.FromUtc(2023, 1, 2 + d, 9, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(8), PunchKind.Out, _emp));
        }
        var day5Start = Instant.FromUtc(2023, 1, 6, 9, 0);
        punches.Add(TestEntityCreator.CreateTestPunch(day5Start, PunchKind.In, _emp));
        punches.Add(TestEntityCreator.CreateTestPunch(day5Start + Duration.FromHours(12), PunchKind.Out, _emp));

        var result = PayCalculator.Calculate(punches, Context());
        var week = result.Workweeks[0];

        // straight 44×20=880; OT premium 4×0.5×20=40 → 920 (unchanged total from before this feature)
        Assert.Equal(920m, result.GrossPay);
        Assert.Equal(5, week.Shifts.Count);

        var day5 = week.Shifts.Single(s => s.ShiftDate == new LocalDate(2023, 1, 6));
        var regularLine = day5.LineItems.Single(l => l.Type == PayLineType.Regular);
        Assert.Equal(12m, regularLine.Hours);
        Assert.Equal(240m, regularLine.Amount);   // full pay for ALL 12 hrs, at the actual rate

        Assert.Equal(20m, regularLine.BaseRate);
        Assert.Equal(1.0m, regularLine.Multiplier);

        var otLine = day5.LineItems.Single(l => l.Type == PayLineType.OvertimePremium);
        Assert.Equal(4m, otLine.Hours);            // only the OT portion of this one pair
        Assert.Equal(40m, otLine.Amount);
        Assert.Equal("OVERTIME", otLine.Code);
        Assert.Equal(20m, otLine.BaseRate);
        Assert.Equal(0.5m, otLine.Multiplier);

        foreach (var s in week.Shifts.Where(s => s.ShiftDate != new LocalDate(2023, 1, 6)))
            Assert.DoesNotContain(s.LineItems, l => l.Type == PayLineType.OvertimePremium);
    }

    [Fact]
    public void OneShift_StraddlingBothOvertimeAndDoubletime_ProducesTwoSeparateLines()
    {
        // A single 15-hour day under CA daily OT: 8 reg + 4 OT (8-12) + 3 DT (>12), all from the
        // SAME punch pair. One combined "OvertimePremium" line couldn't carry a single honest
        // Multiplier here, so it must split into a 0.5x OVERTIME line and a 1.0x DOUBLETIME line.
        var rule = new PayRule();
        rule.OvertimeRule.HasDailyOvertime = true;
        var punches = new List<Punch>
        {
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 9, 0), PunchKind.In, _emp),
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 0, 0), PunchKind.Out, _emp),   // 15h
        };

        var result = PayCalculator.Calculate(punches, Context(rule));
        var shift = result.Workweeks[0].Shifts.Single();

        // straight 15×20=300; OT premium 4×0.5×20=40; DT premium 3×1.0×20=60 → 400
        Assert.Equal(400m, result.GrossPay);

        var ot = shift.LineItems.Single(l => l.Code == "OVERTIME");
        Assert.Equal(4m, ot.Hours);
        Assert.Equal(40m, ot.Amount);
        Assert.Equal(0.5m, ot.Multiplier);

        var dt = shift.LineItems.Single(l => l.Code == "DOUBLETIME");
        Assert.Equal(3m, dt.Hours);
        Assert.Equal(60m, dt.Amount);
        Assert.Equal(1.0m, dt.Multiplier);

        Assert.All(new[] { ot, dt }, l => Assert.Equal(l.Amount, l.Hours * l.BaseRate!.Value * l.Multiplier!.Value));
    }

    [Fact]
    public void PerShiftBreakdown_EachLineItem_CarriesItsShiftsIdentity()
    {
        var punches = Week(3, 8);
        // Give each day's In punch a distinct Id so distinct shifts get distinct anchors
        for (int i = 0; i < punches.Count; i += 2)
            punches[i] = punches[i] with { Id = 100 + i };

        var result = PayCalculator.Calculate(punches, Context());
        var week = result.Workweeks[0];

        Assert.Equal(3, week.Shifts.Count);
        foreach (var shiftPay in week.Shifts)
        {
            Assert.All(shiftPay.LineItems, l => Assert.Equal(shiftPay.ShiftDate, l.ShiftDate));
            Assert.All(shiftPay.LineItems, l => Assert.Equal(shiftPay.AnchorPunchId, l.AnchorPunchId));
        }
        Assert.Equal(3, week.Shifts.Select(s => s.AnchorPunchId).Distinct().Count());
    }

    [Fact]
    public void ShiftGross_SumsToWorkweekGross_SumsToOverallGrossPay()
    {
        var result = PayCalculator.Calculate(Week(5, 10), Context());
        var week = result.Workweeks[0];

        Assert.Equal(week.Gross, week.Shifts.Sum(s => s.Gross));
        Assert.Equal(result.GrossPay, week.Gross);
    }

    [Fact]
    public void WorkweekPay_LineItems_IsFlattenedShiftsLineItems()
    {
        var result = PayCalculator.Calculate(Week(3, 8), Context());
        var week = result.Workweeks[0];

        Assert.Equal(week.Shifts.SelectMany(s => s.LineItems), week.LineItems);
    }

    [Fact]
    public void SplitAcrossTwoWorkweeks_ProducesTwoWorkweekPays()
    {
        // Fri/Sat (Jan 6/7) in week of Jan 1; Sun/Mon (Jan 8/9) in week of Jan 8
        var punches = new List<Punch>();
        foreach (var day in new[] { 6, 7, 8, 9 })
        {
            var start = Instant.FromUtc(2023, 1, day, 9, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(8), PunchKind.Out, _emp));
        }

        var result = PayCalculator.Calculate(punches, Context());
        Assert.Equal(2, result.Workweeks.Count);
        Assert.Equal(4 * 8 * 20m, result.GrossPay);
    }
}
