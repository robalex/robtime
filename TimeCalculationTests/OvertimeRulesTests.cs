using NodaTime;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using Xunit;

namespace TimeCalculationTests;

public class OvertimeRulesTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    // A WorkDay with a single shift of the given hours and consecutive-day position.
    private WorkDay Day(LocalDate date, decimal hours, int consecutive)
    {
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 8, 0), PunchKind.In, _emp);
        var outP = TestEntityCreator.CreateTestPunch(
            Instant.FromUtc(2023, 1, 2, 8, 0) + Duration.FromHours((double)hours), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = 20m }] };
        return new WorkDay { Date = date, Shifts = [shift], ConsecutiveDayNumber = consecutive };
    }

    private Workweek Week(params WorkDay[] days) =>
        new() { StartDate = new LocalDate(2023, 1, 1), Days = days };

    // ── Federal ──

    [Fact]
    public void Federal_UnderForty_AllRegular()
    {
        var week = Week(Day(new LocalDate(2023, 1, 2), 35m, 1));
        var a = new FederalOvertimeRule().Allocate(week);
        Assert.Equal(35m, a.RegularHours);
        Assert.Equal(0m, a.OvertimeHours);
    }

    [Fact]
    public void Federal_OverForty_ExcessIsOvertime()
    {
        // 5 days × 10 hrs = 50 → 40 regular, 10 OT
        var days = Enumerable.Range(0, 5)
            .Select(i => Day(new LocalDate(2023, 1, 2).PlusDays(i), 10m, i + 1))
            .ToArray();
        var a = new FederalOvertimeRule().Allocate(Week(days));
        Assert.Equal(40m, a.RegularHours);
        Assert.Equal(10m, a.OvertimeHours);
        Assert.Equal(0m, a.DoubletimeHours);
    }

    // ── California daily ──

    [Fact]
    public void California_NineHourDay_OneHourOvertime()
    {
        var a = new CaliforniaOvertimeRule().Allocate(Week(Day(new LocalDate(2023, 1, 2), 9m, 1)));
        Assert.Equal(8m, a.RegularHours);
        Assert.Equal(1m, a.OvertimeHours);
        Assert.Equal(0m, a.DoubletimeHours);
    }

    [Fact]
    public void California_ThirteenHourDay_HasDoubletime()
    {
        // 8 reg + 4 OT (8–12) + 1 DT (>12)
        var a = new CaliforniaOvertimeRule().Allocate(Week(Day(new LocalDate(2023, 1, 2), 13m, 1)));
        Assert.Equal(8m, a.RegularHours);
        Assert.Equal(4m, a.OvertimeHours);
        Assert.Equal(1m, a.DoubletimeHours);
    }

    [Fact]
    public void California_SeventhConsecutiveDay_FirstEightAtOvertime_RestDoubletime()
    {
        // Seventh consecutive day, 10 hrs → 8 OT + 2 DT, no regular
        var a = new CaliforniaOvertimeRule().Allocate(Week(Day(new LocalDate(2023, 1, 8), 10m, 7)));
        Assert.Equal(0m, a.RegularHours);
        Assert.Equal(8m, a.OvertimeHours);
        Assert.Equal(2m, a.DoubletimeHours);
    }

    [Fact]
    public void California_WeeklyOvertimeOnRegularHoursBeyondForty()
    {
        // 6 days × 7 hrs = 42, each day under 8 so no daily OT; 2 hrs weekly OT
        var days = Enumerable.Range(0, 6)
            .Select(i => Day(new LocalDate(2023, 1, 2).PlusDays(i), 7m, i + 1))
            .ToArray();
        var a = new CaliforniaOvertimeRule().Allocate(Week(days));
        Assert.Equal(40m, a.RegularHours);
        Assert.Equal(2m, a.OvertimeHours);
        Assert.Equal(0m, a.DoubletimeHours);
    }

    // ── Output views ──

    [Fact]
    public void OvertimeResult_ComposedAndPremiumViews_Reconcile()
    {
        var alloc = new OvertimeAllocation { RegularHours = 40m, OvertimeHours = 8m, DoubletimeHours = 2m };
        var result = new OvertimeResult { Allocation = alloc, RegularRate = 20m };

        // Composed: 40×20 + 8×30 + 2×40 = 800 + 240 + 80 = 1120
        Assert.Equal(1120m, result.TotalPay);
        // Premium: straight 50×20=1000, premium 8×10 + 2×20 = 120 → 1120
        Assert.Equal(1000m, result.StraightTime);
        Assert.Equal(120m, result.OvertimePremium);
        Assert.Equal(result.TotalPay, result.StraightTime + result.OvertimePremium);
    }

    // ── Factory ──

    [Fact]
    public void Factory_NoDailyOrSeventh_ReturnsFederal()
    {
        var rule = OvertimeRuleFactory.FromConfig(new OvertimeRule());
        Assert.IsType<FederalOvertimeRule>(rule);
    }

    [Fact]
    public void Factory_DailyEnabled_ReturnsCalifornia()
    {
        var rule = OvertimeRuleFactory.FromConfig(new OvertimeRule { HasDailyOvertime = true });
        Assert.IsType<CaliforniaOvertimeRule>(rule);
    }
}
