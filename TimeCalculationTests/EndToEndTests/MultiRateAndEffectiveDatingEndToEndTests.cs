
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
}
