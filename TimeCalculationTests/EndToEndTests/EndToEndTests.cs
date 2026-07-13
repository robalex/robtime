using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

/// <summary>
/// End-to-end confidence suite: every test starts from raw Punch objects and runs the whole
/// pipeline via PayCalculator.Calculate (or, for retroactive bonus, the real Workweek objects the
/// pipeline produces), asserting hand-computed expected pay. Grouped by feature area so the file
/// reads as a map of what's been proven to work together, not just in isolation.
///
/// This suite is what found (and this file's fixes proved) three real crash bugs: PairEnricher,
/// ShiftBuilder, and ShiftDater all used to unconditionally dereference PunchPair.InPunch, which
/// is null for an orphan Out (an Out punch with no preceding In). Every existing unit test for
/// those stages happened to pass a pair with InPunch set, so the bug only surfaced once stages
/// were chained together — exactly what end-to-end testing is for.
/// </summary>
public class EndToEndTests
{
    private static Instant At(int day, int hour, int minute = 0) => Instant.FromUtc(2023, 1, day, hour, minute);

    // Instance field, not static: xunit creates a fresh EndToEndTests instance per [Fact], but a
    // static field would persist across the whole test run, so the counter (and thus every
    // AnchorPunchId assertion below) would depend on what order tests happened to execute in.
    private int _currentPunchId = 1;

    private Punch In(Employee emp, Instant t, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.In, emp, _currentPunchId++) with { PositionId = positionId };
    private Punch Out(Employee emp, Instant t, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(t, PunchKind.Out, emp, _currentPunchId++) with { PositionId = positionId };

    // ══════════════════════════════════════════════════════════════════════
    // Pairing & orphans
    // ══════════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════════
    // Overtime
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FederalOvertime_OverFortyHours_PaysHalfTimePremium()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 25m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        for (int d = 2; d <= 6; d++) { punches.Add(In(emp, At(d, 8))); punches.Add(Out(emp, At(d, 17))); }

        var result = PayCalculator.Calculate(punches, ctx);

        // 45 hrs @ $25 = 1125 straight; OT premium 5 × 0.5 × 25 = 62.5 → 1187.5
        Assert.Equal(1187.5m, result.GrossPay);
        Assert.Equal(40m, result.Workweeks[0].RegularHours);
        Assert.Equal(5m, result.Workweeks[0].OvertimeHours);
    }

    [Fact]
    public void California_FourteenHourDay_HasDoubletime()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { OvertimeRule = new OvertimeRule { HasDailyOvertime = true } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch> { In(emp, At(2, 6)), Out(emp, At(2, 20)) };   // 14 hrs

        var result = PayCalculator.Calculate(punches, ctx);

        // 8 reg + 4 OT (8-12) + 2 DT (>12): straight 14×20=280; OT 4×0.5×20=40; DT 2×1×20=40 → 360
        Assert.Equal(360m, result.GrossPay);
        Assert.Equal(8m, result.Workweeks[0].RegularHours);
        Assert.Equal(4m, result.Workweeks[0].OvertimeHours);
        Assert.Equal(2m, result.Workweeks[0].DoubletimeHours);
    }

    [Fact]
    public void California_SevenConsecutiveDays_SeventhDayAndWeeklyCapBothApply()
    {
        // Sun Jan 1 - Sat Jan 7 2023, 8 hrs/day, single workweek (default anchor = Sunday).
        // Days 1-6 (applyDaily branch): 8 reg hrs each = 48 total before the weekly cap.
        // Day 7 (7th-consecutive-day branch): 8 hrs, all <= 8 -> entirely at the OT tier.
        // Weekly cap: 48 regular > 40 -> 8 more hours reclassified regular -> overtime.
        // Final: Regular=40, Overtime=8(day7)+8(weekly cap)=16, Doubletime=0.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { OvertimeRule = new OvertimeRule { HasDailyOvertime = true, HasSeventhDayRule = true } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        for (int d = 1; d <= 7; d++) { punches.Add(In(emp, At(d, 9))); punches.Add(Out(emp, At(d, 17))); }

        var result = PayCalculator.Calculate(punches, ctx);
        var week = result.Workweeks[0];

        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(16m, week.OvertimeHours);
        Assert.Equal(0m, week.DoubletimeHours);
        // straight 56×20=1120; RROP=1120/56=20; OT premium=16×0.5×20=160 → 1280
        Assert.Equal(1280m, result.GrossPay);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Multi-rate & effective dating
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PayRuleChange_MidWeek_RoundingRuleDiffersByDate()
    {
        // Rule A (through Jan 3): rounds to nearest 15 min. Rule B (from Jan 4): nearest 30 min.
        // The same raw punch time (9:10) rounds differently depending on which rule is active
        // for that date, proving PayRule effective-dating flows through Stage 1 end-to-end.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var ruleA = new PayRule { RoundingRule = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 } };
        var ruleB = new PayRule { RoundingRule = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 30 } };
        var ctx = new PipelineContext(emp,
        [
            new PayRuleAssignment(ruleA, new LocalDate(2000, 1, 1), new LocalDate(2023, 1, 3)),
            new PayRuleAssignment(ruleB, new LocalDate(2023, 1, 4)),
        ], []);

        var punches = new List<Punch>
        {
            In(emp, At(2, 9, 10)), Out(emp, At(2, 17)),   // Mon (Rule A): 9:10 -> 9:15 -> 7.75h
            In(emp, At(5, 9, 10)), Out(emp, At(5, 17)),   // Thu (Rule B): 9:10 -> 9:00 -> 8h
        };

        var result = PayCalculator.Calculate(punches, ctx);

        // Mon 7.75×20=155; Thu 8×20=160 → 315, no OT (15.75 total hrs)
        Assert.Equal(315m, result.GrossPay);
        Assert.Equal(0m, result.Workweeks[0].OvertimeHours);
    }

    [Fact]
    public void PositionRateChange_ExactlyAtOvernightShiftMidpoint_SplitsPairAndPaysBothRates()
    {
        // Position rate changes at midnight Jan 4. A single overnight punch pair (22:00 Jan 3 ->
        // 06:00 Jan 4) straddles that boundary, so PunchPairer splits it into two sub-pairs, which
        // ShiftBuilder then re-merges into ONE shift (zero gap between the split pieces) — a
        // direct end-to-end proof that effective-dated position splitting and the per-pair pay
        // breakdown cooperate correctly.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
        var posEarly = new Position { Id = 1, BaseRate = 20m };
        var posLate = new Position { Id = 1, BaseRate = 30m };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [
                new EmployeePositionAssignment(posEarly, new LocalDate(2000, 1, 1), new LocalDate(2023, 1, 3)),
                new EmployeePositionAssignment(posLate, new LocalDate(2023, 1, 4)),
            ]);

        var punches = new List<Punch> { In(emp, At(3, 22)), Out(emp, At(4, 6)) };

        var result = PayCalculator.Calculate(punches, ctx);
        var week = result.Workweeks[0];

        // 2h @ $20 = 40, 6h @ $30 = 180 -> 220, no OT (8 total hrs)
        Assert.Equal(220m, result.GrossPay);
        Assert.Single(week.Shifts);   // the split sub-pairs merge back into one shift

        var regularLines = week.Shifts[0].LineItems.Where(l => l.Type == PayLineType.Regular).ToList();
        Assert.Equal(2, regularLines.Count);
        Assert.Equal(220m, regularLines.Sum(l => l.Amount));
        Assert.Contains(regularLines, l => l.Amount == 40m);
        Assert.Contains(regularLines, l => l.Amount == 180m);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Rounding
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NearestIntervalRounding_ChangesFinalPay()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { RoundingRule = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // In 9:07 -> rounds to 9:00; Out 17:08 -> rounds to 17:15
        var punches = new List<Punch> { In(emp, At(2, 9, 7)), Out(emp, At(2, 17, 8)) };

        var result = PayCalculator.Calculate(punches, ctx);

        // 8.25 hrs @ $20 = 165
        Assert.Equal(165m, result.GrossPay);
        Assert.Equal(8.25m, result.Workweeks[0].RegularHours);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtype inference (Break/Lunch) driving premium outcomes
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoClassifiedLunch_SatisfiesCaMeal_NoPremiumCharged()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // 3h, then a 30-min gap (matches ExpectedLunchLengthMinutes exactly -> auto-classified
        // Lunch, not Break), then 4.5h. Lunch begins at worked-hour 3, well inside the 5-hour window.
        var punches = new List<Punch>
        {
            In(emp, At(2, 9)), Out(emp, At(2, 12)),
            In(emp, At(2, 12, 30)), Out(emp, At(2, 17)),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(150m, result.GrossPay);   // 7.5h @ $20, no premium
        Assert.DoesNotContain(result.LineItems, l => l.Type == PayLineType.Premium);
    }

    [Fact]
    public void ShortGap_AutoClassifiedAsBreakNotLunch_CaMealViolated()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var rule = new PayRule { ActivePremiumCodes = new HashSet<string> { "CA_MEAL" } };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(rule, new LocalDate(2000, 1, 1))], []);

        // Same total shape, but the gap is 15 min (matches ExpectedBreakLengthMinutes) ->
        // auto-classified as Break, not Lunch, so it can't satisfy the meal requirement.
        var punches = new List<Punch>
        {
            In(emp, At(2, 9)), Out(emp, At(2, 12)),
            In(emp, At(2, 12, 15)), Out(emp, At(2, 17)),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 7.75×20=155; RROP=20; premium=1×20=20 → 175
        Assert.Equal(175m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Type == PayLineType.Premium && l.Amount == 20m);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Bonuses & fixed hours
    // ══════════════════════════════════════════════════════════════════════

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
                with { Hours = 5m, CountsTowardRegularRate = countsTowardRegularRate });
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

    // ══════════════════════════════════════════════════════════════════════
    // Differentials
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OvernightAndHolidayDifferentials_StackAndFeedRegularRate_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var holiday = new DifferentialRule
        {
            Code = "HOLIDAY", HolidaysOnly = true,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m,
        };
        var overnight = new DifferentialRule
        {
            Code = "NIGHT", WindowStart = new LocalTime(22, 0), WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 3m,
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
            Code = "SHIFT10", AdjustmentType = DifferentialAdjustmentType.Multiplier, AdjustmentValue = 0.10m,
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
            Code = "HOL_BONUS", HolidaysOnly = true,
            AdjustmentType = DifferentialAdjustmentType.FixedBonus, AdjustmentValue = 50m,
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
    public void ExclusivityGroup_OnlyHighestDifferentialApplies_EndToEnd()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var low = new DifferentialRule { Code = "LOW", ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m };
        var high = new DifferentialRule { Code = "HIGH", ExclusivityGroup = "premium",
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], [], [low, high]);

        var punches = new List<Punch> { In(emp, At(2, 9)), Out(emp, At(2, 17)) };   // 8h

        var result = PayCalculator.Calculate(punches, ctx);

        // straight 8×20=160; only HIGH applies: 8×5=40 → 200 (not 160+16+40=216)
        Assert.Equal(200m, result.GrossPay);
        Assert.Contains(result.LineItems, l => l.Code == "HIGH");
        Assert.DoesNotContain(result.LineItems, l => l.Code == "LOW");
    }

    // ══════════════════════════════════════════════════════════════════════
    // State premiums
    // ══════════════════════════════════════════════════════════════════════

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
        Assert.Single(result.LineItems.Where(l => l.Type == PayLineType.Premium));
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

    // ══════════════════════════════════════════════════════════════════════
    // DST
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FallBackTransition_ExtraHour_PaidCorrectly_EndToEnd()
    {
        // 11 PM Nov 4 EDT -> 3 AM Nov 5 EST: the repeated 1 AM hour means 5 real hours elapsed,
        // not 4. Runs through the FULL pipeline (rounding, subtyping, pairing, dating, RROP,
        // overtime, summarizing) in the employee's real timezone, not just the punch-pairing stage.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "America/New_York", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>
        {
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 3, 0), PunchKind.In, emp),
            TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 8, 0), PunchKind.Out, emp),
        };

        var result = PayCalculator.Calculate(punches, ctx);

        Assert.Equal(100m, result.GrossPay);   // 5 real hrs @ $20
        Assert.Equal(5m, result.Workweeks[0].RegularHours);
        Assert.Equal(new LocalDate(2023, 11, 4), result.Workweeks[0].Shifts.Single().ShiftDate);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Retroactive bonus recalculation (derived from real pipeline-produced Workweeks)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RetroactiveBonus_AcrossTwoRealWorkweeks_DerivedFromRawPunches()
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var ctx = new PipelineContext(emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], []);

        var punches = new List<Punch>();
        foreach (int startDay in new[] { 2, 9 })   // Mon Jan 2 (week of Jan 1) and Mon Jan 9 (week of Jan 8)
        {
            for (int d = startDay; d < startDay + 5; d++)
            {
                punches.Add(In(emp, At(d, 8)));
                punches.Add(Out(emp, At(d, 18)));   // 10 hrs/day × 5 = 50 hrs/week
            }
        }

        var weeks = BuildWeeks(punches, ctx);
        Assert.Equal(2, weeks.Count);

        var result = RetroactiveBonusRecalculator.Recalculate(400m, weeks, new FederalOvertimeRule(), emp.MinimumWage);

        // Each week: 50 hrs (40 reg + 10 OT), equal hours -> $200 bonus/week.
        // Rate increase = 200/50 = $4/hr; additional OT premium = 10×0.5×4 = $20/week -> $40 total.
        Assert.Equal(2, result.PerWeek.Count);
        Assert.All(result.PerWeek, w => Assert.Equal(200m, w.AllocatedBonus));
        Assert.All(result.PerWeek, w => Assert.Equal(20m, w.AdditionalOvertimePremium));
        Assert.Equal(40m, result.AdditionalOvertimePremium);
    }

    // Mirrors PayCalculator.PrepareShifts + CalculateWeeklyPay's grouping, stopping short of pay
    // calculation, to get the real Workweek objects the pipeline produces from raw punches.
    private static IReadOnlyList<Workweek> BuildWeeks(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var rounded = PunchRounder.RoundPunches(punches, ctx);
        var subtyped = PunchSubtypeInferrer.InferPunchSubtypes(rounded, ctx);
        var (pairs, fixedEntries) = PunchPairer.PairPunches(subtyped, ctx);
        var enriched = PairEnricher.AttachPositionAndRateToPunchPairs(pairs, ctx);
        var shifts = ShiftBuilder.BuildShifts(enriched, fixedEntries, ctx);
        var dated = ShiftDater.AssignDatesToShifts(shifts, ctx);
        var days = WorkDayGrouper.Execute(dated, ctx);
        return WorkweekGrouper.Execute(days, ctx);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Kitchen sink: multiple features cooperating in one shift
    // ══════════════════════════════════════════════════════════════════════

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
