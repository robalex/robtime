using NodaTime;
using TimeCalculation.Calculation.Premiums;
using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;
using Xunit;

namespace TimeCalculationTests;

public class PremiumRulesTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
    private static readonly PremiumContext Ctx = new() { RegularRate = 20m };

    // Builds a shift from work segments (hours) separated by gaps (minutes + subtype).
    private Shift Build(decimal[] workHours, params (decimal minutes, PunchSubtype type)[] gaps)
    {
        var t = Instant.FromUtc(2023, 1, 2, 9, 0);
        var pairs = new List<PunchPair>();
        for (int i = 0; i < workHours.Length; i++)
        {
            PunchSubtype? inSub = i > 0 ? gaps[i - 1].type : null;
            PunchSubtype? outSub = i < gaps.Length ? gaps[i].type : null;
            var inP = TestEntityCreator.CreateTestPunch(t, PunchKind.In, _emp) with { Subtype = inSub };
            var outT = t + Duration.FromHours((double)workHours[i]);
            var outP = TestEntityCreator.CreateTestPunch(outT, PunchKind.Out, _emp) with { Subtype = outSub };
            pairs.Add(new PunchPair { InPunch = inP, OutPunch = outP, Rate = 20m });
            t = outT;
            if (i < gaps.Length) t += Duration.FromMinutes((double)gaps[i].minutes);
        }
        return new Shift { ShiftDate = new LocalDate(2023, 1, 2), PunchPairs = pairs };
    }

    // ── CA meal ──

    [Fact]
    public void CaMeal_CompliantLunchByFifthHour_NoPremium()
    {
        var shift = Build([4m, 4m], (30m, PunchSubtype.Lunch));
        var r = new CaMealPremiumRule().Calculate(shift, Ctx);
        Assert.False(r.Violated);
        Assert.Equal(0m, r.Amount);
    }

    [Fact]
    public void CaMeal_NoLunch_OneHourAtRegularRate()
    {
        var shift = Build([8m]);
        var rule = new CaMealPremiumRule();
        Assert.True(rule.Applies(shift, Ctx));
        var r = rule.Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(1m, r.Hours);
        Assert.Equal(20m, r.Amount);
    }

    [Fact]
    public void CaMeal_LunchAfterFifthHour_Violated()
    {
        var shift = Build([6m, 2m], (30m, PunchSubtype.Lunch));  // lunch begins after 6 worked hours
        var r = new CaMealPremiumRule().Calculate(shift, Ctx);
        Assert.True(r.Violated);
    }

    [Fact]
    public void CaMeal_ShortShift_DoesNotApply()
    {
        var shift = Build([4m]);
        Assert.False(new CaMealPremiumRule().Applies(shift, Ctx));
    }

    [Fact]
    public void CaMeal_TwelveHourShift_BothMealsTimely_NoPremium()
    {
        // 12h: 1st lunch begins at worked-hour 4, 2nd at worked-hour 9 (both within window)
        var shift = Build([4m, 5m, 3m], (30m, PunchSubtype.Lunch), (30m, PunchSubtype.Lunch));
        var r = new CaMealPremiumRule().Calculate(shift, Ctx);
        Assert.False(r.Violated);
    }

    [Fact]
    public void CaMeal_TwelveHourShift_LateSecondMeal_Violated()
    {
        // 12h: 1st lunch at worked-hour 4 (ok), 2nd at worked-hour 11 (past the 10th hour) → violation
        var shift = Build([4m, 7m, 1m], (30m, PunchSubtype.Lunch), (30m, PunchSubtype.Lunch));
        var r = new CaMealPremiumRule().Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(1m, r.Hours);   // still capped at one hour per day
    }

    [Fact]
    public void CaMeal_BothOverrides_Waived()
    {
        var shift = Build([8m]);
        var ctx = Ctx with { Overrides = [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver] };
        var r = new CaMealPremiumRule().Calculate(shift, ctx);
        Assert.True(r.Violated);
        Assert.True(r.Waived);
        Assert.Equal(0m, r.Amount);
    }

    [Fact]
    public void CaMeal_SupervisorOnly_NotWaived()
    {
        var shift = Build([8m]);
        var ctx = Ctx with { Overrides = [OverrideKind.SupervisorApproval] };
        var r = new CaMealPremiumRule().Calculate(shift, ctx);
        Assert.False(r.Waived);
        Assert.Equal(20m, r.Amount);
    }

    // ── CA rest ──

    [Fact]
    public void CaRest_FewerBreaksThanRequired_Violated()
    {
        // 8 hrs → 2 rests required; only 1 clocked Break
        var shift = Build([4m, 4m], (10m, PunchSubtype.Break));
        var r = new CaRestPremiumRule().Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(20m, r.Amount);
    }

    [Fact]
    public void CaRest_EnoughBreaks_NoPremium()
    {
        var shift = Build([3m, 3m, 2m], (10m, PunchSubtype.Break), (10m, PunchSubtype.Break));
        var r = new CaRestPremiumRule().Calculate(shift, Ctx);
        Assert.False(r.Violated);
    }

    [Fact]
    public void CaRest_NotWaivable_EvenWithBothOverrides()
    {
        var shift = Build([4m, 4m], (10m, PunchSubtype.Break));
        var ctx = Ctx with { Overrides = [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver] };
        var r = new CaRestPremiumRule().Calculate(shift, ctx);
        Assert.True(r.Violated);
        Assert.False(r.Waived);
        Assert.Equal(20m, r.Amount);
    }

    // ── CO rest ──

    [Fact]
    public void CoRest_FewerBreaksThanRequired_Violated_NotWaivable()
    {
        var shift = Build([8m]);
        var ctx = Ctx with { Overrides = [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver] };
        var r = new CoRestPremiumRule().Calculate(shift, ctx);
        Assert.True(r.Violated);
        Assert.False(r.Waived);
    }

    // ── PR meal (overtime rate) ──

    [Fact]
    public void PrMeal_NoMeal_OneHourAtOvertimeRate()
    {
        var shift = Build([7m]);
        var r = new PrMealPremiumRule().Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(30m, r.Amount);   // 20 × 1.5
    }

    // ── OR meal ──

    [Fact]
    public void OrMeal_SixHourShiftNoMeal_OneHourRegular()
    {
        var shift = Build([6m]);
        var rule = new OrMealPremiumRule();
        Assert.True(rule.Applies(shift, Ctx));
        var r = rule.Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(20m, r.Amount);
    }

    // ── WA meal (half-hour remedy) ──

    [Fact]
    public void WaMeal_NoMeal_HalfHourAtRegularRate()
    {
        var shift = Build([6m]);
        var r = new WaMealPremiumRule().Calculate(shift, Ctx);
        Assert.True(r.Violated);
        Assert.Equal(0.5m, r.Hours);
        Assert.Equal(10m, r.Amount);   // 0.5 × 20
    }

    // ── Waiver evaluator ──

    [Theory]
    [InlineData(WaiverPolicy.NotWaivable, true, true, false)]
    [InlineData(WaiverPolicy.BothRequired, true, true, true)]
    [InlineData(WaiverPolicy.BothRequired, true, false, false)]
    [InlineData(WaiverPolicy.SupervisorOnly, true, false, true)]
    [InlineData(WaiverPolicy.EmployeeOnly, false, true, true)]
    public void WaiverEvaluator_HonorsPolicy(WaiverPolicy policy, bool sup, bool emp, bool expected)
    {
        var overrides = new List<OverrideKind>();
        if (sup) overrides.Add(OverrideKind.SupervisorApproval);
        if (emp) overrides.Add(OverrideKind.EmployeeWaiver);
        Assert.Equal(expected, WaiverEvaluator.IsWaived(policy, overrides));
    }

    // ── Registry ──

    [Fact]
    public void Registry_ResolvesKnownCodes_SkipsUnknown()
    {
        var rules = PremiumRegistry.Resolve(["CA_MEAL", "CO_REST", "NOPE"]);
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.Code == "CA_MEAL");
        Assert.Contains(rules, r => r.Code == "CO_REST");
    }
}
