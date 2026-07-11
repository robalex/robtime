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

    private Shift NoMealShift(LocalDate date, int startHour, int hours, int anchorId = 0)
    {
        var start = date.AtMidnight().InUtc().ToInstant() + Duration.FromHours(startHour);
        var inP  = TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp) with { Id = anchorId };
        var outP = TestEntityCreator.CreateTestPunch(start + Duration.FromHours(hours), PunchKind.Out, _emp);
        return new Shift { ShiftDate = date, PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = 20m }] };
    }

    [Fact]
    public void MealPremium_CappedToOnePerWorkday_AcrossSplitShifts()
    {
        var date = new LocalDate(2023, 1, 2);
        // Split-shift day: two shifts, both >5h with no meal → two violations, but only one premium owed
        var shifts = new[] { NoMealShift(date, 6, 8), NoMealShift(date, 18, 6) };
        var result = PremiumApplier.Execute(shifts, CtxWith("CA_MEAL"), _ => 20m);

        var mealPremiums = result.SelectMany(s => s.Premiums).Where(p => p.Code == "CA_MEAL").ToList();
        Assert.Equal(2, mealPremiums.Count(p => p.Violated));   // both violations recorded for audit
        var paid = Assert.Single(mealPremiums, p => p.IsPaid);   // but only one is paid
        Assert.Equal(20m, paid.Amount);
    }

    [Fact]
    public void MealPremium_OnDifferentDays_EachPaid()
    {
        // The cap is per workday, not global — two single-shift days each owe a premium
        var shifts = new[]
        {
            NoMealShift(new LocalDate(2023, 1, 2), 9, 8),
            NoMealShift(new LocalDate(2023, 1, 3), 9, 8),
        };
        var result = PremiumApplier.Execute(shifts, CtxWith("CA_MEAL"), _ => 20m);

        Assert.Equal(2, result.SelectMany(s => s.Premiums).Count(p => p.Code == "CA_MEAL" && p.IsPaid));
    }

    [Fact]
    public void WaivingThePaidPremium_MovesChargeToOtherShiftSameDay()
    {
        var date = new LocalDate(2023, 1, 2);
        var morning = NoMealShift(date, 6, 8, anchorId: 101);
        var evening = NoMealShift(date, 18, 6, anchorId: 202);
        var ctx = CtxWith("CA_MEAL");

        // Baseline: the morning shift (first) carries the single paid premium
        var baseline = PremiumApplier.Execute([morning, evening], ctx, _ => 20m);
        Assert.Equal(101, PaidMeal(baseline).AnchorPunchId);

        // Waive the morning meal premium (CA meal needs supervisor AND employee)
        var waived = PremiumApplier.Execute([morning, evening], ctx, _ => 20m,
            s => s.PunchPairs[0].InPunch!.Id == 101
                ? [OverrideKind.SupervisorApproval, OverrideKind.EmployeeWaiver]
                : []);

        // The charge moves to the evening shift; the morning is waived (still one premium that day)
        Assert.Equal(202, PaidMeal(waived).AnchorPunchId);
        var meals = waived.SelectMany(s => s.Premiums).Where(p => p.Code == "CA_MEAL").ToList();
        Assert.Contains(meals, p => p.Waived && p.AnchorPunchId == 101);
    }

    [Fact]
    public void PremiumResult_SurfacesWaiverPolicyAndIdentity()
    {
        var ctx = CtxWith("CA_MEAL", "CA_REST");
        var shift = NoMealShift(new LocalDate(2023, 1, 2), 9, 8, anchorId: 55);
        var result = PremiumApplier.Execute([shift], ctx, _ => 20m);

        var meal = result[0].Premiums.Single(p => p.Code == "CA_MEAL");
        var rest = result[0].Premiums.Single(p => p.Code == "CA_REST");

        Assert.Equal(WaiverPolicy.BothRequired, meal.WaiverPolicy);   // UI: show a "needs both" waive control
        Assert.Equal(WaiverPolicy.NotWaivable, rest.WaiverPolicy);    // UI: no waive control
        Assert.Equal(55, meal.AnchorPunchId);                         // stable identity for override round-trip
        Assert.Equal(new LocalDate(2023, 1, 2), meal.ShiftDate);
    }

    private static PremiumResult PaidMeal(IReadOnlyList<Shift> shifts) =>
        shifts.SelectMany(s => s.Premiums).Single(p => p.Code == "CA_MEAL" && p.IsPaid);

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
