using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Generative (seeded, deterministic) tests asserting the invariants that must hold for every input:
/// pipeline purity, orchestrator idempotency, allocation conservation, and non-negativity.
/// FsCheck is not referenced; a seeded Random gives reproducible generation without the dependency.
/// </summary>
public class PropertyBasedTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };

    private static PipelineContext Context(PayRule? rule = null)
    {
        var emp = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var pos = new Position { Id = 5, BaseRate = 20m };
        return new PipelineContext(
            emp,
            [new PayRuleAssignment(rule ?? new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(pos, new LocalDate(2000, 1, 1))]);
    }

    // Deterministically generate a valid In/Out punch sequence from a seed.
    private List<Punch> GeneratePunches(int seed)
    {
        var rng = new Random(seed);
        var punches = new List<Punch>();
        int days = rng.Next(1, 10);
        for (int d = 0; d < days; d++)
        {
            if (rng.NextDouble() < 0.15) continue;          // some days off
            int startHour = rng.Next(5, 12);
            int length = rng.Next(2, 13);
            var start = Instant.FromUtc(2023, 1, 2 + d, startHour, 0);
            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp));
            punches.Add(TestEntityCreator.CreateTestPunch(start + Duration.FromHours(length), PunchKind.Out, _emp));
        }
        return punches;
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(7)] [InlineData(13)] [InlineData(42)]
    [InlineData(99)] [InlineData(123)] [InlineData(555)] [InlineData(2024)] [InlineData(31337)]
    public void PayCalculator_IsIdempotent(int seed)
    {
        var punches = GeneratePunches(seed);
        var a = PayCalculator.Calculate(punches, Context());
        var b = PayCalculator.Calculate(punches, Context());

        Assert.Equal(a.GrossPay, b.GrossPay);
        Assert.Equal(a.LineItems, b.LineItems);
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void Pipeline_Stages_ArePure_InputNotMutated_OutputDeterministic(int seed)
    {
        var punches = GeneratePunches(seed);
        int originalCount = punches.Count;
        var ctx = Context();

        var r1 = Stage1_RoundPunches.Execute(punches, ctx);
        var r2 = Stage1_RoundPunches.Execute(punches, ctx);

        Assert.Equal(originalCount, punches.Count);            // input untouched
        Assert.Equal(r1.Select(p => p.EffectiveTime), r2.Select(p => p.EffectiveTime));
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void OvertimeAllocation_ConservesTotalHours(int seed)
    {
        var punches = GeneratePunches(seed);
        var caRule = new PayRule();
        caRule.OvertimeRule.HasDailyOvertime = true;
        caRule.OvertimeRule.HasSeventhDayRule = true;
        var ctx = Context(caRule);

        var weeks = BuildWeeks(punches, ctx);
        var rule = OvertimeRuleFactory.FromConfig(caRule.OvertimeRule);

        foreach (var week in weeks)
        {
            var alloc = rule.Allocate(week);
            Assert.Equal(week.TotalHours, alloc.TotalHours);
            Assert.True(alloc.RegularHours >= 0 && alloc.OvertimeHours >= 0 && alloc.DoubletimeHours >= 0);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void GrossPay_IsNeverNegative_AndAtLeastStraightTime(int seed)
    {
        var punches = GeneratePunches(seed);
        var result = PayCalculator.Calculate(punches, Context());

        Assert.True(result.GrossPay >= 0);
        foreach (var w in result.Workweeks)
        {
            var straight = w.LineItems.Where(l => l.Type == PayLineType.Regular).Sum(l => l.Amount);
            Assert.True(w.Gross >= straight);   // premium view: gross = straight + premium ≥ straight
        }
    }

    private static IReadOnlyList<Workweek> BuildWeeks(List<Punch> punches, PipelineContext ctx)
    {
        var rounded = Stage1_RoundPunches.Execute(punches, ctx);
        var subtyped = Stage2_InferPunchSubtypes.Execute(rounded, ctx);
        var (pairs, fixedEntries) = Stage3_PairPunches.Execute(subtyped, ctx);
        var enriched = Stage4_EnrichPairs.Execute(pairs, ctx);
        var shifts = Stage5_BuildShifts.Execute(enriched, fixedEntries, ctx);
        var dated = Stage6_DateShifts.Execute(shifts, ctx);
        var days = Stage9_GroupIntoDays.Execute(dated, ctx);
        return Stage10_GroupIntoWeeks.Execute(days, ctx);
    }
}
