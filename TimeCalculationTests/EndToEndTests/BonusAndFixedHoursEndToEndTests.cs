using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class BonusAndFixedHoursEndToEndTests : EndToEndTests
{
    [Fact]
    public void NonDiscretionaryBonus_RaisesRegularRate_AndTheOvertimePremiumWithIt()
    {
        // 50 hrs @ $20 (10 OT hrs, federal) plus a single $100 non-discretionary bonus. The bonus
        // must raise the regular rate BEFORE the overtime premium is priced from it — the wrong
        // answer if the bonus were ignored is 10x0.5x20=$100, not the correct 10x0.5x22=$110.
        // A discretionary bonus of the same size is paid but must NOT move the rate at all.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var pos = new Position { Id = 1, BaseRate = 20m };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(pos, new LocalDate(2000, 1, 1))]);

        List<Punch> BuildPunches(BonusKind bonusKind)
        {
            var punches = new List<Punch>();
            for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 8))); punches.Add(Out(emp, At(d, 18))); }
            punches.Add(TestEntityCreator.CreateTestPunch(At(3, 12), PunchKind.FixedDollar, emp)
                with { Amount = 100m, BonusKind = bonusKind });
            return punches;
        }

        var nonDiscretionary = PayCalculator.Calculate(BuildPunches(BonusKind.NonDiscretionary), ctx);
        var discretionary = PayCalculator.Calculate(BuildPunches(BonusKind.Discretionary), ctx);

        // Non-discretionary: RROP=(1000+100)/50=22; OT premium=10x0.5x22=110; +bonus 100 -> 1210
        Assert.Equal(22m, nonDiscretionary.Workweeks[0].RegularRate);
        var otLine = nonDiscretionary.LineItems.Single(l => l.Code == "OVERTIME");
        Assert.Equal(110m, otLine.Amount);
        Assert.Equal(22m, otLine.BaseRate);
        Assert.NotEqual(100m, otLine.Amount);   // 10x0.5x20 -- wrong: as if the bonus were ignored
        Assert.Equal(1210m, nonDiscretionary.GrossPay);

        // Discretionary: same $100 is still paid (as a Bonus line), but excluded from RROP
        // entirely -> rate stays 20, OT premium stays 10x0.5x20=100, gross 1000+100+100=1200
        Assert.Equal(20m, discretionary.Workweeks[0].RegularRate);
        Assert.Equal(100m, discretionary.LineItems.Single(l => l.Code == "OVERTIME").Amount);
        Assert.Equal(1200m, discretionary.GrossPay);
        Assert.Contains(discretionary.LineItems, l => l.Type == PayLineType.Bonus && l.Amount == 100m);
    }

    [Fact]
    public void FixedHours_CountsTowardRegularRate_ChangesOvertimePremium_EndToEnd()
    {
        // 45 clock hrs @ $20 (5 OT hrs, federal). Plus a 5-hr FixedHours entry valued at a
        // minimum wage ($30) higher than the clock rate. With the flag off, it's excluded from
        // RROP entirely; with it on, it raises the RROP (and thus the OT premium) — proving the
        // flag actually reaches the regular-rate calculation through the whole pipeline.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 30m };
        var pos = new Position { Id = 1, BaseRate = 20m };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(pos, new LocalDate(2000, 1, 1))]);

        List<Punch> BuildPunches(bool countsTowardRegularRate)
        {
            var punches = new List<Punch>();
            for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 8))); punches.Add(Out(emp, At(d, 17))); }
            punches.Add(TestEntityCreator.CreateTestPunch(At(4, 12), PunchKind.FixedHours, emp)
                with
            { Hours = 5m, CountsTowardRegularRate = countsTowardRegularRate });
            return punches;
        }

        var withoutFlag = PayCalculator.Calculate(BuildPunches(false), ctx);
        var withFlag = PayCalculator.Calculate(BuildPunches(true), ctx);

        // Flag off: RROP=900/45=20; OT premium=5×0.5×20=50; +FixedHours pay(5×30=150) → 1100
        Assert.Equal(20m, withoutFlag.Workweeks[0].RegularRate);
        Assert.Equal(1100m, withoutFlag.GrossPay);

        // Flag on: RROP=(900+5×30)/(45+5)=1050/50=21; OT premium=5×0.5×21=52.5; +150 → 1102.5
        Assert.Equal(21m, withFlag.Workweeks[0].RegularRate);
        Assert.Equal(1102.5m, withFlag.GrossPay);
    }
}
