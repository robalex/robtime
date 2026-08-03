using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Metamorphic properties for <see cref="RegularRateCalculator"/> — relationships between two runs
/// that must hold whatever the implementation, rather than pinned expected values.
///
/// Deliberately NOT the naive-oracle treatment used for <c>PayPeriodCalculator</c> and the overtime
/// rules. Those two compute in closed form, so a differently-shaped second implementation genuinely
/// fails differently. This one is already a flat accumulate-then-divide loop; a second version would
/// also sum and divide, transcribed from the same doc comment, and the two would tend to be wrong in
/// the same places — which is precisely when differential testing stops earning its keep.
///
/// The actual risk surface here is the four inclusion filters (<c>IsMissingPunch</c>,
/// <c>CountsTowardRegularRate</c>, <c>BonusKind</c>, and the nested day→shift→pair traversal), and
/// the properties below are aimed at those: each one changes exactly one thing about a week and
/// asserts the rate moves by exactly the right amount, or not at all.
/// </summary>
public class RegularRateCalculatorPropertyTests
{
    private const decimal Tolerance = 0.0000001m;
    private const decimal MinimumWage = 15m;

    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = MinimumWage };

    private static readonly Instant WeekStart = Instant.FromUtc(2023, 1, 2, 0, 0);

    private PunchPair Pair(Instant start, decimal hours, decimal rate) => new()
    {
        InPunch = TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp),
        OutPunch = TestEntityCreator.CreateTestPunch(
            start + Duration.FromHours((double)hours), PunchKind.Out, _emp),
        Rate = rate,
    };

    /// <summary>A week of 1–4 days, each with 1–2 shifts of 1–3 pairs, at rates spread widely enough
    /// that the weighted average is genuinely weighted rather than incidentally uniform.</summary>
    private Workweek GenerateWeek(Random rng)
    {
        var days = new List<WorkDay>();
        int dayCount = rng.Next(1, 5);

        for (int d = 0; d < dayCount; d++)
        {
            var shifts = new List<Shift>();
            int shiftCount = rng.Next(1, 3);

            for (int s = 0; s < shiftCount; s++)
            {
                var pairs = new List<PunchPair>();
                int pairCount = rng.Next(1, 4);

                for (int p = 0; p < pairCount; p++)
                {
                    var start = WeekStart + Duration.FromHours(d * 24 + s * 10 + p * 3);
                    pairs.Add(Pair(start, rng.Next(1, 9) * 0.25m, rng.Next(60, 241) * 0.25m));
                }

                shifts.Add(new Shift { PunchPairs = pairs, ShiftDate = new LocalDate(2023, 1, 2).PlusDays(d) });
            }

            days.Add(new WorkDay
            {
                Date = new LocalDate(2023, 1, 2).PlusDays(d),
                Shifts = shifts,
                ConsecutiveDayNumber = d + 1,
            });
        }

        return new Workweek { StartDate = new LocalDate(2023, 1, 1), Days = days };
    }

    private static Workweek WithFirstShiftEntries(Workweek week, IReadOnlyList<Punch> entries)
    {
        var days = week.Days.ToList();
        var shifts = days[0].Shifts.ToList();
        shifts[0] = shifts[0] with { FixedEntries = [.. shifts[0].FixedEntries, .. entries] };
        days[0] = days[0] with { Shifts = shifts };
        return week with { Days = days };
    }

    private Punch FixedDollar(decimal amount, BonusKind bonusKind) =>
        TestEntityCreator.CreateTestPunch(WeekStart, PunchKind.FixedDollar, _emp)
            with { Amount = amount, BonusKind = bonusKind };

    private Punch FixedHours(decimal hours, bool countsTowardRegularRate) =>
        TestEntityCreator.CreateTestPunch(WeekStart, PunchKind.FixedHours, _emp)
            with { Hours = hours, CountsTowardRegularRate = countsTowardRegularRate };

    // ── Mathematical properties of a weighted average ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void RegularRate_LiesBetweenTheLowestAndHighestRateWorked(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            var rates = week.Days
                .SelectMany(d => d.Shifts)
                .SelectMany(s => s.PunchPairs)
                .Select(p => p.Rate ?? 0m)
                .ToList();

            var result = RegularRateCalculator.Calculate(week, MinimumWage);

            // With no bonuses, differentials or fixed entries in play, the rate is a pure weighted
            // average and cannot escape the range of its inputs.
            Assert.True(
                result.RegularRate >= rates.Min() - Tolerance && result.RegularRate <= rates.Max() + Tolerance,
                $"RROP {result.RegularRate} outside [{rates.Min()}, {rates.Max()}].");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void ScalingEveryRate_ScalesTheRegularRateByTheSameFactor(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            decimal factor = rng.Next(2, 9) * 0.5m;

            var scaled = week with
            {
                Days = [.. week.Days.Select(d => d with
                {
                    Shifts = [.. d.Shifts.Select(s => s with
                    {
                        PunchPairs = [.. s.PunchPairs.Select(p => p with { Rate = (p.Rate ?? 0m) * factor })],
                    })],
                })],
            };

            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);
            var actual = RegularRateCalculator.Calculate(scaled, MinimumWage);

            AssertClose(baseline.RegularRate * factor, actual.RegularRate, $"scaling by {factor}");
            AssertClose(baseline.TotalHours, actual.TotalHours, "hours must not move when only rates scale");
        }
    }

    // ── One thing changes, by exactly the right amount ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void AddingNonDiscretionaryBonus_RaisesRateByBonusOverHours_AndLeavesHoursAlone(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            decimal bonus = rng.Next(1, 401) * 0.25m;

            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);
            var withBonus = RegularRateCalculator.Calculate(
                WithFirstShiftEntries(week, [FixedDollar(bonus, BonusKind.NonDiscretionary)]), MinimumWage);

            AssertClose(
                baseline.RegularRate + (bonus / baseline.TotalHours), withBonus.RegularRate,
                $"a ${bonus} non-discretionary bonus over {baseline.TotalHours}h");

            // A dollar bonus is not hours worked and must never touch the denominator.
            AssertClose(baseline.TotalHours, withBonus.TotalHours, "bonus must not add hours");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void AddingCountingFixedHours_MovesRateTowardMinimumWage_ByTheDocumentedFormula(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            decimal extraHours = rng.Next(1, 17) * 0.25m;

            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);
            var withEntry = RegularRateCalculator.Calculate(
                WithFirstShiftEntries(week, [FixedHours(extraHours, countsTowardRegularRate: true)]), MinimumWage);

            // Documented behaviour: hours join the denominator, pay joins the numerator at minimum
            // wage — so the result is the blend of the old rate and minimum wage.
            var expectedNumerator = (baseline.RegularRate * baseline.TotalHours) + (extraHours * MinimumWage);
            var expectedHours = baseline.TotalHours + extraHours;

            AssertClose(expectedHours, withEntry.TotalHours, $"{extraHours}h of counting FixedHours");
            AssertClose(expectedNumerator / expectedHours, withEntry.RegularRate, $"{extraHours}h at minimum wage");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void AddingADifferential_RaisesRateByAmountOverHours_AndLeavesHoursAlone(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            decimal amount = rng.Next(1, 401) * 0.25m;

            var days = week.Days.ToList();
            var shifts = days[0].Shifts.ToList();
            shifts[0] = shifts[0] with
            {
                Differentials = [.. shifts[0].Differentials, new AppliedDifferential
                {
                    Code = "SHIFT_DIFF",
                    Hours = 1m,
                    Amount = amount,
                }],
            };
            days[0] = days[0] with { Shifts = shifts };

            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);
            var withDifferential = RegularRateCalculator.Calculate(week with { Days = days }, MinimumWage);

            // Counted exactly once, on the one shift carrying it — a day holding several shifts must
            // not multiply it, which is the classic shape of a misplaced accumulation.
            AssertClose(
                baseline.RegularRate + (amount / baseline.TotalHours), withDifferential.RegularRate,
                $"a ${amount} differential over {baseline.TotalHours}h " +
                $"({days[0].Shifts.Count} shifts on the carrying day)");

            AssertClose(baseline.TotalHours, withDifferential.TotalHours, "differential must not add hours");
        }
    }

    // ── Things that must change nothing at all ──

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void ExcludedEntries_AreCompletelyInert(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);

            // Each of these is documented as excluded, and "excluded" has to mean the whole result is
            // byte-identical — not merely that the rate happens to land in the same place.
            var inertEntries = new List<Punch>
            {
                FixedDollar(rng.Next(1, 500), BonusKind.Discretionary),
                FixedHours(rng.Next(1, 17) * 0.25m, countsTowardRegularRate: false),
            };

            foreach (var entry in inertEntries)
            {
                var actual = RegularRateCalculator.Calculate(WithFirstShiftEntries(week, [entry]), MinimumWage);
                Assert.True(
                    baseline == actual,
                    $"{entry.Kind}/{entry.BonusKind}/counts={entry.CountsTowardRegularRate} changed the result: " +
                    $"{baseline.RegularRate}@{baseline.TotalHours}h became {actual.RegularRate}@{actual.TotalHours}h.");
            }
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void AnOrphanPunch_ContributesNeitherHoursNorEarnings(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);

            // An In with no Out (someone forgot to clock out) has no knowable duration. It must not
            // reach the denominator — an orphan silently contributing 0 hours at a real rate would
            // be harmless, but contributing 0 hours while still being *counted* would deflate the
            // rate for everyone else on the timecard.
            var days = week.Days.ToList();
            var shifts = days[0].Shifts.ToList();
            var orphan = new PunchPair
            {
                InPunch = TestEntityCreator.CreateTestPunch(WeekStart, PunchKind.In, _emp),
                OutPunch = null,
                Rate = rng.Next(60, 241) * 0.25m,
            };
            shifts[0] = shifts[0] with { PunchPairs = [.. shifts[0].PunchPairs, orphan] };
            days[0] = days[0] with { Shifts = shifts };

            var actual = RegularRateCalculator.Calculate(week with { Days = days }, MinimumWage);

            Assert.True(
                baseline == actual,
                $"An orphan In punch at rate {orphan.Rate} changed the result: " +
                $"{baseline.RegularRate}@{baseline.TotalHours}h became {actual.RegularRate}@{actual.TotalHours}h.");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void SplittingAPairInTwo_AtTheSameRate_ChangesNothing(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);

            // Every pair becomes two adjacent halves at its own rate. Same hours, same money, far
            // more objects for the traversal to walk — a boundary split (PunchPairer does this for
            // real at effective-date boundaries) must be invisible to the rate.
            var split = week with
            {
                Days = [.. week.Days.Select(d => d with
                {
                    Shifts = [.. d.Shifts.Select(s => s with
                    {
                        PunchPairs = [.. s.PunchPairs.SelectMany(SplitInHalf)],
                    })],
                })],
            };

            var actual = RegularRateCalculator.Calculate(split, MinimumWage);

            AssertClose(baseline.TotalHours, actual.TotalHours, "hours after splitting every pair");
            AssertClose(baseline.RegularRate, actual.RegularRate, "rate after splitting every pair");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void ReversingDayAndShiftOrder_ChangesNothing(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 100; i++)
        {
            var week = GenerateWeek(rng);
            var baseline = RegularRateCalculator.Calculate(week, MinimumWage);

            var reversed = week with
            {
                Days = [.. week.Days
                    .Select(d => d with { Shifts = [.. d.Shifts.Reverse()] })
                    .Reverse()],
            };

            var actual = RegularRateCalculator.Calculate(reversed, MinimumWage);

            Assert.True(
                baseline == actual,
                $"Order changed the result: {baseline.RegularRate}@{baseline.TotalHours}h " +
                $"became {actual.RegularRate}@{actual.TotalHours}h.");
        }
    }

    private IEnumerable<PunchPair> SplitInHalf(PunchPair pair)
    {
        var start = pair.InPunch!.EffectiveTime;
        var midpoint = start + Duration.FromHours((double)(pair.TotalHours / 2m));

        yield return pair with
        {
            OutPunch = TestEntityCreator.CreateTestPunch(midpoint, PunchKind.Out, _emp),
        };

        yield return pair with
        {
            InPunch = TestEntityCreator.CreateTestPunch(midpoint, PunchKind.In, _emp),
        };
    }

    private static void AssertClose(decimal expected, decimal actual, string context)
    {
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance,
            $"{context}: expected {expected}, got {actual} (difference {Math.Abs(expected - actual)}).");
    }
}
