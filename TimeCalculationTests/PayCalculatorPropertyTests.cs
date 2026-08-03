using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Metamorphic properties over the whole <see cref="PayCalculator"/> pipeline: each one transforms a
/// week of punches in a way whose effect on pay is known exactly, then asserts pay moved that way
/// (or not at all). Distinct from <c>PropertyBasedTests</c>, which asserts single-run invariants
/// (purity, idempotency, non-negativity) rather than relationships between two runs.
///
/// End-to-end deliberately: per CLAUDE.md, the three real crash bugs this repo has found were all
/// invisible to per-stage tests and only surfaced once stages were chained. These run raw punches
/// through <see cref="PayCalculator.Calculate"/>, so a stage that quietly drops or duplicates work
/// shows up as money.
///
/// All of these use a default <see cref="PayRule"/>, which carries no active premium or differential
/// codes. That keeps each transformation's effect on pay analytically known — with a meal-premium
/// rule live, for instance, lengthening a shift could legitimately add a penalty, and the
/// monotonicity property below would be asserting something weaker than it appears to.
/// </summary>
public class PayCalculatorPropertyTests
{
    private const decimal Tolerance = 0.0000001m;

    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private static PipelineContext Context()
    {
        var employee = new Employee { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };
        var position = new Position { Id = 5, BaseRate = 20m };
        return new PipelineContext(
            employee,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(position, new LocalDate(2000, 1, 1))]);
    }

    /// <summary>A week of In/Out pairs with real punch ids, starting <paramref name="dayOffset"/>
    /// days after the base date so callers can place a batch in a chosen workweek.</summary>
    private List<Punch> GeneratePunches(Random rng, int dayOffset = 0, int idBase = 1)
    {
        var punches = new List<Punch>();
        int days = rng.Next(3, 8);
        int nextId = idBase;

        for (int d = 0; d < days; d++)
        {
            if (rng.NextDouble() < 0.15)
            {
                continue;
            }

            int startHour = rng.Next(5, 12);
            int length = rng.Next(2, 11);
            var start = Instant.FromUtc(2023, 1, 2, startHour, 0) + Duration.FromDays(dayOffset + d);

            punches.Add(TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp, nextId++));
            punches.Add(TestEntityCreator.CreateTestPunch(
                start + Duration.FromHours(length), PunchKind.Out, _emp, nextId++));
        }

        return punches;
    }

    // ── Workweek isolation ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void AddingPunchesInAnotherWorkweek_LeavesThisWeeksPayUntouched(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 40; i++)
        {
            var thisWeek = GeneratePunches(rng);
            // 21 days out lands in a different workweek whatever WorkweekStartDay is configured.
            var laterWeek = GeneratePunches(rng, dayOffset: 21, idBase: 1000);

            var alone = PayCalculator.Calculate(thisWeek, Context());
            var together = PayCalculator.Calculate([.. thisWeek, .. laterWeek], Context());

            // The FLSA workweek is the unit of computation — regular rate and overtime are both
            // scoped to it — so an unrelated week must not perturb this one by a cent.
            foreach (var week in alone.Workweeks)
            {
                var same = together.Workweeks.SingleOrDefault(w => w.WeekStart == week.WeekStart);

                Assert.True(
                    same is not null,
                    $"Workweek {week.WeekStart} vanished once a later week's punches were added.");

                // Compared field by field rather than with record `==`: WorkweekPay holds an
                // IReadOnlyList, and synthesized record equality compares that by reference, so `==`
                // is always false across two separate Calculate calls and would "pass" this test by
                // failing for the wrong reason.
                Assert.Equal(week.RegularRate, same!.RegularRate);
                Assert.Equal(week.RegularHours, same.RegularHours);
                Assert.Equal(week.OvertimeHours, same.OvertimeHours);
                Assert.Equal(week.DoubletimeHours, same.DoubletimeHours);
                Assert.Equal(week.Gross, same.Gross);

                // PayLineItem is all scalar fields, so xunit's element-wise collection comparison
                // does give real structural equality here.
                Assert.Equal(week.LineItems, same.LineItems);
            }
        }
    }

    // ── Splitting a pair ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void SplittingEveryPairAtItsMidpoint_LeavesGrossPayUnchanged(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 40; i++)
        {
            var punches = GeneratePunches(rng);
            var baseline = PayCalculator.Calculate(punches, Context());

            // Each In/Out becomes In/Out/In/Out with the interior pair meeting at a single instant:
            // same hours worked, same money owed, twice as many objects for the pipeline to carry.
            // PunchPairer does exactly this for real when splitting at effective-date boundaries.
            var split = new List<Punch>();
            int nextId = 500;

            for (int p = 0; p < punches.Count; p += 2)
            {
                var inPunch = punches[p];
                var outPunch = punches[p + 1];
                var midpoint = inPunch.PunchTime + ((outPunch.PunchTime - inPunch.PunchTime) / 2);

                split.Add(inPunch);
                split.Add(TestEntityCreator.CreateTestPunch(midpoint, PunchKind.Out, _emp, nextId++));
                split.Add(TestEntityCreator.CreateTestPunch(midpoint, PunchKind.In, _emp, nextId++));
                split.Add(outPunch);
            }

            var actual = PayCalculator.Calculate(split, Context());

            AssertClose(baseline.GrossPay, actual.GrossPay, $"gross pay after splitting {punches.Count / 2} pairs");
        }
    }

    // ── Monotonicity ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void WorkingLongerAtTheSameRate_NeverDecreasesGrossPay(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 40; i++)
        {
            var punches = GeneratePunches(rng);
            var baseline = PayCalculator.Calculate(punches, Context());

            // Push the final Out punch later, leaving everything else alone.
            decimal extraHours = rng.Next(1, 17) * 0.25m;
            var extended = punches.ToList();
            var last = extended[^1];
            extended[^1] = last with { PunchTime = last.PunchTime + Duration.FromHours((double)extraHours) };

            var actual = PayCalculator.Calculate(extended, Context());

            // Hours only ever move into a higher-paying tier, never a lower one, so more time on the
            // clock at an unchanged rate cannot pay less. A rounding or overtime-attribution bug that
            // loses hours would surface here as pay going backwards.
            Assert.True(
                actual.GrossPay >= baseline.GrossPay - Tolerance,
                $"Adding {extraHours}h dropped gross pay from {baseline.GrossPay} to {actual.GrossPay}.");
        }
    }

    // ── Itemization reconciles to the allocation ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void LineItems_ReconcileToTheWeeksOvertimeAllocation(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 40; i++)
        {
            var result = PayCalculator.Calculate(GeneratePunches(rng), Context());

            foreach (var week in result.Workweeks)
            {
                // Deliberately NOT "line items sum to gross" — Gross is *defined* as that sum, so the
                // assertion could never fail. This instead crosses the two things that are computed
                // separately: PaySummarizer's per-pair itemization against OvertimeCalculator's
                // week-level allocation. They must describe the same hours.
                var allocated = week.RegularHours + week.OvertimeHours + week.DoubletimeHours;
                var itemizedHours = week.LineItems
                    .Where(l => l.Type == PayLineType.Regular)
                    .Sum(l => l.Hours);

                AssertClose(allocated, itemizedHours, $"week {week.WeekStart}: allocated vs itemized hours");

                // The overtime premium is attributed back to individual pairs by a convention (see
                // PaySummarizer); whatever it picks, the hours it hands out must total exactly what
                // the allocation said was owed — no pair double-counted, none skipped.
                var premiumHours = week.LineItems
                    .Where(l => l.Type == PayLineType.OvertimePremium && l.Code == "OVERTIME")
                    .Sum(l => l.Hours);
                var doubletimeHours = week.LineItems
                    .Where(l => l.Type == PayLineType.OvertimePremium && l.Code == "DOUBLETIME")
                    .Sum(l => l.Hours);

                AssertClose(week.OvertimeHours, premiumHours, $"week {week.WeekStart}: overtime premium hours");
                AssertClose(week.DoubletimeHours, doubletimeHours, $"week {week.WeekStart}: doubletime premium hours");
            }
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void EveryLineItemAmount_MatchesItsOwnRateAndMultiplier(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 40; i++)
        {
            var result = PayCalculator.Calculate(GeneratePunches(rng), Context());

            // PayLineItem's doc comment promises Amount == Hours × BaseRate × Multiplier whenever
            // both are non-null. A UI showing "8h × $20 × 1.5" beside an Amount that doesn't equal
            // that product is how people lose trust in a pay statement.
            foreach (var line in result.LineItems.Where(l => l.BaseRate is not null && l.Multiplier is not null))
            {
                AssertClose(
                    line.Hours * line.BaseRate!.Value * line.Multiplier!.Value,
                    line.Amount,
                    $"{line.Type}/{line.Code} on {line.ShiftDate}: {line.Hours}h × {line.BaseRate} × {line.Multiplier}");
            }
        }
    }

    private static void AssertClose(decimal expected, decimal actual, string context)
    {
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance,
            $"{context}: expected {expected}, got {actual} (difference {Math.Abs(expected - actual)}).");
    }
}
