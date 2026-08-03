using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculationTests.Oracles;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Differential tests pitting <see cref="PayPeriodCalculator"/> against
/// <see cref="NaivePayPeriodCalculator"/> (see that class for why a second implementation earns its
/// keep). These complement rather than replace <c>PayPeriodCalculatorTests</c>: those pin specific
/// hand-computed windows so the *intent* is anchored, while these hunt the shapes hand-written
/// examples systematically miss — dates far either side of the anchor, exact period boundaries, and
/// the extreme default anchor.
///
/// Seeded <see cref="Random"/> rather than FsCheck, matching <c>PropertyBasedTests</c>' existing
/// approach: reproducible generation with no added dependency.
/// </summary>
public class PayPeriodCalculatorOracleTests
{
    private static readonly LocalDate BaseDate = new(2023, 1, 1);

    // What PayRule.PayPeriodAnchor actually holds when nobody sets one: `default(LocalDate)` is
    // 0001-01-01, so this is a live production configuration rather than a synthetic extreme, and
    // it puts ~740,000 days between anchor and date for the closed-form path to get right.
    private static readonly LocalDate DefaultAnchor = default;

    private static readonly PayPeriodFrequency[] AnchoredFrequencies =
        [PayPeriodFrequency.Weekly, PayPeriodFrequency.BiWeekly];

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void ContainingDate_MatchesNaiveOracle_AcrossRandomDatesAndAnchors(int seed)
    {
        var rng = new Random(seed);
        var frequencies = Enum.GetValues<PayPeriodFrequency>();

        for (int i = 0; i < 400; i++)
        {
            var frequency = frequencies[rng.Next(frequencies.Length)];
            var anchor = BaseDate.PlusDays(rng.Next(-500, 500));

            // Deliberately skewed to span both sides of the anchor by years: dates *before* the
            // anchor are the half of the input space FloorDiv's sign handling exists for.
            var date = BaseDate.PlusDays(rng.Next(-4000, 4000));

            AssertAgrees(frequency, date, anchor);
        }
    }

    [Fact]
    public void ContainingDate_MatchesNaiveOracle_AtDefaultAnchor()
    {
        foreach (var frequency in AnchoredFrequencies)
        {
            foreach (int offset in new[] { 0, 1, 6, 7, 13, 14, 365, 739_000, 739_837 })
            {
                AssertAgrees(frequency, DefaultAnchor.PlusDays(offset), DefaultAnchor);
            }
        }
    }

    [Fact]
    public void ContainingDate_MatchesNaiveOracle_AtExactPeriodBoundaries()
    {
        foreach (var frequency in AnchoredFrequencies)
        {
            int length = frequency == PayPeriodFrequency.Weekly ? 7 : 14;

            // Walk several periods either side of the anchor and probe the four dates where an
            // off-by-one would show: the day before a period opens, its first day, its last day,
            // and the day it hands off to the next.
            for (int index = -3; index <= 3; index++)
            {
                var start = BaseDate.PlusDays(index * length);
                foreach (int probe in new[] { -1, 0, length - 1, length })
                {
                    AssertAgrees(frequency, start.PlusDays(probe), BaseDate);
                }
            }
        }
    }

    [Theory]
    [InlineData(7)] [InlineData(123)] [InlineData(555)]
    public void Generate_TilesTheRangeContiguously_AndEveryPeriodMatchesOracle(int seed)
    {
        var rng = new Random(seed);
        var frequencies = Enum.GetValues<PayPeriodFrequency>();

        for (int i = 0; i < 50; i++)
        {
            var frequency = frequencies[rng.Next(frequencies.Length)];
            var anchor = BaseDate.PlusDays(rng.Next(-500, 500));
            var rangeStart = BaseDate.PlusDays(rng.Next(-1000, 1000));
            var rangeEnd = rangeStart.PlusDays(rng.Next(0, 400));

            var periods = PayPeriodCalculator.Generate(frequency, rangeStart, rangeEnd, anchor);

            Assert.NotEmpty(periods);
            Assert.True(periods[0].Contains(rangeStart), "First period must cover the range start.");
            Assert.True(periods[^1].End >= rangeEnd, "Last period must reach the range end.");

            for (int p = 0; p < periods.Count; p++)
            {
                if (p > 0)
                {
                    Assert.True(
                        periods[p - 1].End.PlusDays(1) == periods[p].Start,
                        $"Gap or overlap between {periods[p - 1].End} and {periods[p].Start} " +
                        $"({frequency}, anchor={anchor}).");
                }

                // Each generated period must be the same one you'd get by asking for the period
                // containing its own start date — Generate and ContainingDate can't disagree.
                AssertAgrees(frequency, periods[p].Start, anchor);
            }
        }
    }

    private static void AssertAgrees(PayPeriodFrequency frequency, LocalDate date, LocalDate anchor)
    {
        var actual = PayPeriodCalculator.ContainingDate(frequency, date, anchor);
        var expected = NaivePayPeriodCalculator.ContainingDate(frequency, date, anchor);

        Assert.True(
            expected == actual,
            $"{frequency} date={date} anchor={anchor}: " +
            $"production returned {actual.Start}..{actual.End}, oracle returned {expected.Start}..{expected.End}");

        // Cheap invariant that needs no oracle at all, but would catch both being wrong the same way.
        Assert.True(actual.Contains(date), $"{frequency}: {actual.Start}..{actual.End} does not contain {date}.");
    }
}
