using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;

namespace TimeCalculationTests.Oracles;

/// <summary>Thresholds and toggles for <see cref="NaiveOvertimeAllocator"/>, mirroring the knobs
/// <see cref="CaliforniaOvertimeRule"/> exposes. Federal is the degenerate case: no daily tiers and
/// no seventh-day rule, leaving only the weekly threshold.</summary>
internal record NaiveOvertimeConfig
{
    internal decimal DailyOvertimeThreshold { get; init; } = 8m;
    internal decimal DailyDoubletimeThreshold { get; init; } = 12m;
    internal decimal WeeklyThreshold { get; init; } = 40m;
    internal bool ApplyDaily { get; init; } = true;
    internal bool ApplySeventhDay { get; init; } = true;

    internal static NaiveOvertimeConfig Federal(decimal weeklyThreshold = 40m) => new()
    {
        WeeklyThreshold = weeklyThreshold,
        ApplyDaily = false,
        ApplySeventhDay = false,
    };
}

/// <summary>
/// A deliberately naive second implementation of the <see cref="IOvertimeRule"/> allocations, used as
/// a test oracle (see <see cref="NaivePayPeriodCalculator"/> for the rationale behind the pattern).
///
/// Production allocates in closed form: per day it sums nested <c>Math.Min</c>/<c>Math.Max</c>
/// expressions into three running buckets, then applies a weekly correction that reaches back and
/// reclassifies already-accumulated regular hours. That is compact and fast, but the interaction
/// between the daily tiers, the seventh-day override, and the weekly fix-up is genuinely hard to
/// verify by reading — which is exactly the shape worth double-implementing.
///
/// This version instead walks the week a quarter-hour at a time and asks, for each individual
/// increment, "which bucket does *this* increment belong to?" — using its position within the day for
/// the daily tiers and a running regular-hours total for the weekly threshold. No bucket is ever
/// reclassified after the fact. Slow and obvious, which is the point.
///
/// Two limitations worth stating. First, it reads <see cref="WorkDay.TotalHours"/>, which both
/// implementations share, so it validates the *allocation*, not the hour summation beneath it.
/// Second, it deliberately mirrors production's reading of the seventh-day rule (whole day is
/// premium; the 8-hour line splits 1.5x from 2x, and none of that day's hours count toward the
/// weekly threshold). If that reading of the law is wrong, both agree and both are wrong — the
/// hand-computed cases in <c>OvertimeRulesTests</c> are what pin the intent.
/// </summary>
internal static class NaiveOvertimeAllocator
{
    /// <summary>Walk granularity. Quarter-hours match how payroll actually rounds, and keeping every
    /// threshold and day total on this grid means no increment ever straddles a tier boundary — see
    /// the precondition check below, which fails loudly rather than quietly comparing wrong numbers.</summary>
    internal const decimal Increment = 0.25m;

    internal static OvertimeAllocation Allocate(Workweek week, NaiveOvertimeConfig config)
    {
        RequireOnIncrementGrid(week, config);

        decimal regular = 0m;
        decimal overtime = 0m;
        decimal doubletime = 0m;

        foreach (var day in week.Days)
        {
            decimal hoursIntoDay = 0m;

            while (hoursIntoDay < day.TotalHours)
            {
                var step = Math.Min(Increment, day.TotalHours - hoursIntoDay);
                var tier = ClassifyIncrement(hoursIntoDay, day.ConsecutiveDayNumber, config);

                if (tier == Tier.Doubletime)
                {
                    doubletime += step;
                }
                else if (tier == Tier.Overtime)
                {
                    overtime += step;
                }
                else if (regular < config.WeeklyThreshold)
                {
                    // Weekly overtime decided here, as each increment is placed, rather than by
                    // reaching back afterwards to reclassify hours already banked as regular.
                    regular += step;
                }
                else
                {
                    overtime += step;
                }

                hoursIntoDay += step;
            }
        }

        return new OvertimeAllocation
        {
            RegularHours = regular,
            OvertimeHours = overtime,
            DoubletimeHours = doubletime,
        };
    }

    private enum Tier
    {
        Regular,
        Overtime,
        Doubletime,
    }

    /// <summary>Which tier the increment beginning at <paramref name="hoursIntoDay"/> falls in,
    /// judged purely by where it sits within its own day. Regular here means "candidate for regular"
    /// — the weekly threshold can still push it to overtime upstream.</summary>
    private static Tier ClassifyIncrement(decimal hoursIntoDay, int consecutiveDayNumber, NaiveOvertimeConfig config)
    {
        Tier tier;

        if (config.ApplySeventhDay && consecutiveDayNumber == 7)
        {
            tier = hoursIntoDay < config.DailyOvertimeThreshold ? Tier.Overtime : Tier.Doubletime;
        }
        else if (!config.ApplyDaily)
        {
            tier = Tier.Regular;
        }
        else if (hoursIntoDay < config.DailyOvertimeThreshold)
        {
            tier = Tier.Regular;
        }
        else if (hoursIntoDay < config.DailyDoubletimeThreshold)
        {
            tier = Tier.Overtime;
        }
        else
        {
            tier = Tier.Doubletime;
        }

        return tier;
    }

    /// <summary>Guards the one assumption the walk depends on: that no increment straddles a tier
    /// boundary. Violating it would make this oracle silently wrong, which is far worse than a
    /// throw — an oracle nobody can trust is worse than no oracle.</summary>
    private static void RequireOnIncrementGrid(Workweek week, NaiveOvertimeConfig config)
    {
        decimal[] thresholds =
        [
            config.DailyOvertimeThreshold,
            config.DailyDoubletimeThreshold,
            config.WeeklyThreshold,
        ];

        foreach (var threshold in thresholds)
        {
            if (threshold % Increment != 0m)
            {
                throw new ArgumentException(
                    $"Threshold {threshold} is not a multiple of {Increment}; the naive walk would " +
                    "straddle a tier boundary and produce a wrong answer.", nameof(config));
            }
        }

        foreach (var day in week.Days)
        {
            if (day.TotalHours % Increment != 0m)
            {
                throw new ArgumentException(
                    $"Day {day.Date} has {day.TotalHours} hours, not a multiple of {Increment}.", nameof(week));
            }
        }
    }
}
