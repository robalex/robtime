using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class DifferentialEndToEndTests : EndToEndTests
{
    [Fact]
    public void OvernightAndHolidayDifferentials_StackAndFeedRegularRate_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var holiday = new DifferentialRule
        {
            Code = "HOLIDAY",
            DayScheduleMode = DayScheduleMode.Holidays,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 5m,
        };
        var overnight = new DifferentialRule
        {
            Code = "NIGHT",
            WindowStart = new LocalTime(22, 0),
            WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3m,
        };
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 2)]);
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [],
            [holiday, overnight], holidays);

        // 22:00 Jan 2 (the holiday) -> 02:00 Jan 3, 4 hrs total
        var punches = new List<Punch> { In(emp, At(2, 22)), Out(emp, At(3, 2)) };

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 4×20=80; holiday diff covers only the 2 hrs on Jan 2 -> 2×5=10;
        // overnight diff covers the whole 4-hr window -> 4×3=12 -> gross 80+10+12=102
        Assert.Equal(102m, result.GrossPay);

        // Code identifies exactly which differential this line is — a UI shouldn't need to parse
        // the human-readable Description to know that.
        var holidayLine = result.LineItems.Single(l => l.Type == PayLineType.Differential && l.Code == "HOLIDAY");
        Assert.Equal(10m, holidayLine.Amount);
        Assert.Equal(5m, holidayLine.BaseRate);    // the configured $/hr for a FlatPerHour differential
        Assert.Equal(1.0m, holidayLine.Multiplier);

        var nightLine = result.LineItems.Single(l => l.Type == PayLineType.Differential && l.Code == "NIGHT");
        Assert.Equal(12m, nightLine.Amount);
        Assert.Equal(3m, nightLine.BaseRate);
        Assert.Equal(1.0m, nightLine.Multiplier);
    }

    [Fact]
    public void MultiplierDifferential_BaseRate_IsBackSolvedFromThePairsOwnRate_EndToEnd()
    {
        // A 10% shift differential on an 8-hr shift at $25/hr: PaySummarizer doesn't re-walk the
        // qualifying pairs to find "the rate" — it solves BaseRate back from the already-known
        // Amount/Hours/AdjustmentValue, so it comes out exact even without re-deriving anything.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 25m };
        var shiftDiff = new DifferentialRule
        {
            Code = "SHIFT10",
            AdjustmentType = DifferentialAdjustmentType.Multiplier,
            AdjustmentValue = 0.10m,
        };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [shiftDiff]);

        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h @ $25 (min-wage fallback)

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 8×25=200; diff 8×25×0.10=20 → 220
        Assert.Equal(220m, result.GrossPay);

        var diffLine = result.LineItems.Single(l => l.Code == "SHIFT10");
        Assert.Equal(20m, diffLine.Amount);
        Assert.Equal(0.10m, diffLine.Multiplier);
        Assert.Equal(25m, diffLine.BaseRate);   // solved back to the pair's own $25 rate
        Assert.Equal(diffLine.Amount, diffLine.Hours * diffLine.BaseRate!.Value * diffLine.Multiplier!.Value);
    }

    [Fact]
    public void FixedBonusDifferential_HasNoBaseRateOrMultiplier_EndToEnd()
    {
        // A flat lump-sum differential (e.g. a holiday bonus paid once regardless of hours) has no
        // per-hour rate to report — BaseRate/Multiplier are null rather than a misleading number.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var holidayBonus = new DifferentialRule
        {
            Code = "HOL_BONUS",
            DayScheduleMode = DayScheduleMode.Holidays,
            AdjustmentType = DifferentialAdjustmentType.FixedBonus,
            AdjustmentValue = 50m,
        };
        var holidays = new HolidayCalendar([new LocalDate(2023, 1, 2)]);
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [holidayBonus], holidays);

        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h, on the holiday

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 8×20=160; fixed bonus +50 → 210
        Assert.Equal(210m, result.GrossPay);
        var diffLine = result.LineItems.Single(l => l.Code == "HOL_BONUS");
        Assert.Equal(50m, diffLine.Amount);
        Assert.Null(diffLine.BaseRate);
        Assert.Null(diffLine.Multiplier);
    }

    [Fact]
    public void RangeHoursThreshold_Met_AcrossShifts_ThatIndividuallyWouldNotQualify_EndToEnd()
    {
        // A loyalty differential ($2/hr) active Mon–Fri and requiring 20 qualifying hours across
        // that range occurrence. Mon–Fri (Jan 2–6 2023) at 5 hrs/day = 25 hrs. No single shift is
        // close to 20, but the range total clears it, so every hour earns the differential.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var loyalty = new DifferentialRule
        {
            Code = "LOYALTY",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Monday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Friday,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
            MinHoursInRange = 20m,
        };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [loyalty]);

        var punches = new List<Punch>();
        for (int day = 2; day <= 6; day++)
        {
            punches.Add(In(emp, At(day, 9)));
            punches.Add(Out(emp, At(day, 14)));   // 5 hrs
        }

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 25×20=500; differential 25×2=50 → 550
        Assert.Equal(550m, result.GrossPay);
        Assert.Equal(50m, result.LineItems.Where(l => l.Code == "LOYALTY").Sum(l => l.Amount));
    }

    [Fact]
    public void RangeHoursThreshold_NotMet_StripsDifferentialEntirely_EndToEnd()
    {
        // Same rule, but only Mon–Wed (3×5 = 15 hrs) < the 20-hr range threshold, so the loyalty
        // differential never qualifies and contributes nothing to gross or the line items.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var loyalty = new DifferentialRule
        {
            Code = "LOYALTY",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Monday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Friday,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
            MinHoursInRange = 20m,
        };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [loyalty]);

        var punches = new List<Punch>();
        for (int day = 2; day <= 4; day++)
        {
            punches.Add(In(emp, At(day, 9)));
            punches.Add(Out(emp, At(day, 14)));   // 5 hrs
        }

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 15×20=300; differential stripped → 300
        Assert.Equal(300m, result.GrossPay);
        Assert.DoesNotContain(result.LineItems, l => l.Code == "LOYALTY");
    }

    [Fact]
    public void ExclusivityGroup_OnlyHighestDifferentialApplies_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var low = new DifferentialRule
        {
            Code = "LOW",
            ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m
        };
        var high = new DifferentialRule
        {
            Code = "HIGH",
            ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 5m
        };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [low, high]);

        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 8×20=160; only HIGH applies: 8×5=40 → 200 (not 160+16+40=216)
        Assert.Equal(200m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Code == "HIGH");
        Assert.DoesNotContain(result.LineItems, l => l.Code == "LOW");
    }
}
