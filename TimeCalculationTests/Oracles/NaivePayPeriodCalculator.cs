using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculationTests.Oracles;

/// <summary>
/// A deliberately naive second implementation of <see cref="TimeCalculation.Calculation.PayPeriodCalculator"/>,
/// used as a test oracle (differential / N-version testing): the production version and this one are
/// compared across generated inputs, and any disagreement is a bug in one of them.
///
/// The value here comes from the two being structured *differently*, not from this one being better.
/// Production computes the containing period in closed form — <c>FloorDiv(daysSinceAnchor, length)</c>
/// over a <see cref="double"/> — which is fast but carries the usual hazards of that shape:
/// off-by-one at boundaries, sign handling for dates before the anchor, and floor-vs-truncate. This
/// version instead *walks* from the anchor in whole-period steps, so it has no division, no modulus,
/// and no floating point to get wrong. It is slow and obviously correct by inspection; that is the
/// entire point. The calendar-driven frequencies get the same treatment for symmetry (enumerate the
/// month, keep the run of days sharing this date's bucket) though they are lower-risk, since the
/// production versions of those read straight off the calendar with no anchor arithmetic.
///
/// Note the blind spot: if both implementations share the same *misunderstanding* of what a pay
/// period is, they will agree and both be wrong. This validates that the production code does what
/// was intended, not that the intent matches the regulation — the hand-computed cases in
/// <c>PayPeriodCalculatorTests</c> are what pin the intent.
/// </summary>
internal static class NaivePayPeriodCalculator
{
    internal static PayPeriod ContainingDate(
        PayPeriodFrequency frequency, LocalDate date, LocalDate anchor = default)
        => frequency switch
        {
            PayPeriodFrequency.Weekly => WalkFromAnchor(frequency, date, anchor, 7),
            PayPeriodFrequency.BiWeekly => WalkFromAnchor(frequency, date, anchor, 14),
            PayPeriodFrequency.SemiMonthly => EnumerateMonth(frequency, date),
            PayPeriodFrequency.Monthly => EnumerateMonth(frequency, date),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
        };

    /// <summary>Steps whole periods out from the anchor until one covers <paramref name="date"/>.
    /// Exactly one of the two loops ever runs: the first walks back when the date precedes the
    /// anchor, the second walks forward when it follows.</summary>
    private static PayPeriod WalkFromAnchor(
        PayPeriodFrequency frequency, LocalDate date, LocalDate anchor, int length)
    {
        var start = anchor;

        while (start > date)
        {
            start = start.PlusDays(-length);
        }

        while (start.PlusDays(length - 1) < date)
        {
            start = start.PlusDays(length);
        }

        return new PayPeriod
        {
            Start = start,
            End = start.PlusDays(length - 1),
            Frequency = frequency,
        };
    }

    /// <summary>Walks the containing calendar month a day at a time and keeps the contiguous run of
    /// days sharing <paramref name="date"/>'s bucket, rather than computing the boundaries.</summary>
    private static PayPeriod EnumerateMonth(PayPeriodFrequency frequency, LocalDate date)
    {
        var daysInBucket = new List<LocalDate>();
        var cursor = new LocalDate(date.Year, date.Month, 1);

        while (cursor.Month == date.Month && cursor.Year == date.Year)
        {
            if (SharesBucket(cursor, date, frequency))
            {
                daysInBucket.Add(cursor);
            }

            cursor = cursor.PlusDays(1);
        }

        return new PayPeriod
        {
            Start = daysInBucket[0],
            End = daysInBucket[^1],
            Frequency = frequency,
        };
    }

    // Monthly puts the whole month in one bucket; semi-monthly splits on the 15th/16th line.
    private static bool SharesBucket(LocalDate a, LocalDate b, PayPeriodFrequency frequency)
        => frequency == PayPeriodFrequency.Monthly || (a.Day <= 15) == (b.Day <= 15);
}
