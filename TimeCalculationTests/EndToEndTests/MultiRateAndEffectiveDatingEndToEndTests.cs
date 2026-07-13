
using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests.EndToEndTests;

public class MultiRateAndEffectiveDatingEndToEndTests : EndToEndTests
{
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

    [Fact]
    public void MultiRate_WeightedRegularRate_DrivesOvertimePremium()
    {
        // 3 days @ pos1 ($20) + 2 days @ pos2 ($30), 10 hrs each = 50 hrs, one workweek. The
        // overtime premium must be priced from the WEIGHTED regular rate across both positions
        // ($24), not either position's rate alone — $20 and $30 are both plausible-looking wrong
        // answers if the weighting were skipped or done incorrectly.
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };
        var pos1 = new Position { Id = 1, BaseRate = 20m };
        var pos2 = new Position { Id = 2, BaseRate = 30m };
        var ctx = new PipelineContext(emp,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [
                new EmployeePositionAssignment(pos1, new LocalDate(2000, 1, 1)),
                new EmployeePositionAssignment(pos2, new LocalDate(2000, 1, 1)),
            ]);

        var punches = new List<Punch>();
        for (int d = 2; d <= 4; d++) { punches.Add(In(emp, At(d, 8), pos1.Id)); punches.Add(Out(emp, At(d, 18), pos1.Id)); }
        for (int d = 5; d <= 6; d++) { punches.Add(In(emp, At(d, 8), pos2.Id)); punches.Add(Out(emp, At(d, 18), pos2.Id)); }

        var result = PayCalculator.Calculate(punches, ctx);
        var week = result.Workweeks[0];

        // straight = 30x20 + 20x30 = 600 + 600 = 1200; RROP = 1200/50 = 24 (not 20, not 30)
        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(10m, week.OvertimeHours);
        Assert.Equal(24m, week.RegularRate);

        // OT premium priced at the weighted rate: 10x0.5x24=120 -- neither single-position rate
        var otLine = result.LineItems.Single(l => l.Code == "OVERTIME");
        Assert.Equal(120m, otLine.Amount);
        Assert.Equal(24m, otLine.BaseRate);
        Assert.Equal(0.5m, otLine.Multiplier);
        Assert.NotEqual(100m, otLine.Amount);   // 10x0.5x20 -- wrong: pos1's rate alone
        Assert.NotEqual(150m, otLine.Amount);   // 10x0.5x30 -- wrong: pos2's rate alone

        // straight 1200 + OT premium 120 = 1320
        Assert.Equal(1320m, result.GrossPay);
    }
}
