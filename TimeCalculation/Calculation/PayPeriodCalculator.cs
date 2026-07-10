using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

/// <summary>
/// Generates pay-period boundaries for a given frequency.  Weekly and bi-weekly periods are
/// measured from a reference <c>anchor</c> date; semi-monthly and monthly periods are derived
/// from the calendar and ignore the anchor.
///
/// This is a scaffold: it produces the date ranges only.  Assigning workweeks to periods and
/// prorating overtime cost across boundaries is a downstream concern (later phase).
/// </summary>
public static class PayPeriodCalculator
{
    /// <summary>Returns the single pay period containing <paramref name="date"/>.</summary>
    public static PayPeriod ContainingDate(
        PayPeriodFrequency frequency, LocalDate date, LocalDate anchor = default)
        => frequency switch
        {
            PayPeriodFrequency.Weekly => FixedLength(frequency, date, anchor, 7),
            PayPeriodFrequency.BiWeekly => FixedLength(frequency, date, anchor, 14),
            PayPeriodFrequency.SemiMonthly => SemiMonthly(date),
            PayPeriodFrequency.Monthly => Monthly(date),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
        };

    /// <summary>Returns every pay period that overlaps the inclusive [rangeStart, rangeEnd] window.</summary>
    public static IReadOnlyList<PayPeriod> Generate(
        PayPeriodFrequency frequency, LocalDate rangeStart, LocalDate rangeEnd, LocalDate anchor = default)
    {
        if (rangeEnd < rangeStart)
            throw new ArgumentException("rangeEnd must be on or after rangeStart.", nameof(rangeEnd));

        var periods = new List<PayPeriod>();
        var period = ContainingDate(frequency, rangeStart, anchor);
        while (period.Start <= rangeEnd)
        {
            periods.Add(period);
            period = ContainingDate(frequency, period.End.PlusDays(1), anchor);
        }
        return periods;
    }

    private static PayPeriod FixedLength(PayPeriodFrequency freq, LocalDate date, LocalDate anchor, int length)
    {
        int daysSinceAnchor = Period.DaysBetween(anchor, date);
        int index = FloorDiv(daysSinceAnchor, length);
        var start = anchor.PlusDays(index * length);
        return new PayPeriod { Start = start, End = start.PlusDays(length - 1), Frequency = freq };
    }

    private static PayPeriod SemiMonthly(LocalDate date)
    {
        if (date.Day <= 15)
        {
            return new PayPeriod
            {
                Start = new LocalDate(date.Year, date.Month, 1),
                End = new LocalDate(date.Year, date.Month, 15),
                Frequency = PayPeriodFrequency.SemiMonthly,
            };
        }

        return new PayPeriod
        {
            Start = new LocalDate(date.Year, date.Month, 16),
            End = date.With(DateAdjusters.EndOfMonth),
            Frequency = PayPeriodFrequency.SemiMonthly,
        };
    }

    private static PayPeriod Monthly(LocalDate date) => new()
    {
        Start = new LocalDate(date.Year, date.Month, 1),
        End = date.With(DateAdjusters.EndOfMonth),
        Frequency = PayPeriodFrequency.Monthly,
    };

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
