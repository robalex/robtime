using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

public class PayPeriodCalculatorTests
{
    // Weekly / bi-weekly are measured from an anchor reference date.
    private static readonly LocalDate Anchor = new(2023, 1, 1);   // a Sunday

    [Fact]
    public void Weekly_ContainingDate_ReturnsSevenDayPeriodFromAnchor()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.Weekly, new LocalDate(2023, 1, 4), Anchor);

        Assert.Equal(new LocalDate(2023, 1, 1), p.Start);
        Assert.Equal(new LocalDate(2023, 1, 7), p.End);
        Assert.Equal(7, p.LengthInDays);
    }

    [Fact]
    public void Weekly_DateInSecondPeriod_ReturnsCorrectWindow()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.Weekly, new LocalDate(2023, 1, 10), Anchor);

        Assert.Equal(new LocalDate(2023, 1, 8), p.Start);
        Assert.Equal(new LocalDate(2023, 1, 14), p.End);
    }

    [Fact]
    public void BiWeekly_ContainingDate_ReturnsFourteenDayPeriod()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.BiWeekly, new LocalDate(2023, 1, 20), Anchor);

        Assert.Equal(new LocalDate(2023, 1, 15), p.Start);
        Assert.Equal(new LocalDate(2023, 1, 28), p.End);
        Assert.Equal(14, p.LengthInDays);
    }

    [Fact]
    public void Weekly_DateBeforeAnchor_FloorsIntoPriorPeriod()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.Weekly, new LocalDate(2022, 12, 28), Anchor);

        Assert.Equal(new LocalDate(2022, 12, 25), p.Start);
        Assert.Equal(new LocalDate(2022, 12, 31), p.End);
    }

    [Fact]
    public void SemiMonthly_FirstHalf_IsFirstThroughFifteenth()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.SemiMonthly, new LocalDate(2023, 3, 10));

        Assert.Equal(new LocalDate(2023, 3, 1), p.Start);
        Assert.Equal(new LocalDate(2023, 3, 15), p.End);
    }

    [Fact]
    public void SemiMonthly_SecondHalf_IsSixteenthThroughEndOfMonth()
    {
        // February 2023 has 28 days
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.SemiMonthly, new LocalDate(2023, 2, 20));

        Assert.Equal(new LocalDate(2023, 2, 16), p.Start);
        Assert.Equal(new LocalDate(2023, 2, 28), p.End);
    }

    [Fact]
    public void Monthly_ReturnsWholeCalendarMonth()
    {
        var p = PayPeriodCalculator.ContainingDate(PayPeriodFrequency.Monthly, new LocalDate(2024, 2, 10));

        Assert.Equal(new LocalDate(2024, 2, 1), p.Start);
        Assert.Equal(new LocalDate(2024, 2, 29), p.End);   // 2024 is a leap year
        Assert.Equal(29, p.LengthInDays);
    }

    [Fact]
    public void Generate_BiWeekly_OverTwoMonths_ProducesContiguousPeriods()
    {
        var periods = PayPeriodCalculator.Generate(
            PayPeriodFrequency.BiWeekly, new LocalDate(2023, 1, 1), new LocalDate(2023, 1, 31), Anchor);

        Assert.Equal(3, periods.Count);
        Assert.Equal(new LocalDate(2023, 1, 1), periods[0].Start);
        Assert.Equal(new LocalDate(2023, 1, 15), periods[1].Start);
        Assert.Equal(new LocalDate(2023, 1, 29), periods[2].Start);

        // contiguous, no gaps or overlaps
        for (int i = 1; i < periods.Count; i++)
            Assert.Equal(periods[i - 1].End.PlusDays(1), periods[i].Start);
    }

    [Fact]
    public void Generate_SemiMonthly_AcrossMonthBoundary_ProducesFourHalves()
    {
        var periods = PayPeriodCalculator.Generate(
            PayPeriodFrequency.SemiMonthly, new LocalDate(2023, 1, 5), new LocalDate(2023, 2, 20));

        Assert.Equal(4, periods.Count);
        Assert.Equal(new LocalDate(2023, 1, 1),  periods[0].Start);
        Assert.Equal(new LocalDate(2023, 1, 16), periods[1].Start);
        Assert.Equal(new LocalDate(2023, 2, 1),  periods[2].Start);
        Assert.Equal(new LocalDate(2023, 2, 16), periods[3].Start);
    }

    [Fact]
    public void Generate_InvertedRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => PayPeriodCalculator.Generate(
            PayPeriodFrequency.Weekly, new LocalDate(2023, 2, 1), new LocalDate(2023, 1, 1), Anchor));
    }
}
