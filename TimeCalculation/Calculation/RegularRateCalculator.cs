using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

/// <summary>
/// Stage 11 — Regular rate of pay (FLSA 29 CFR §778), computed per workweek.
///
/// RROP = (Σ straight-time earnings + non-discretionary bonuses + differentials) ÷ total hours worked
///
/// Multi-rate is handled naturally: straight-time earnings sum each pair's hours × its own rate,
/// so dividing by total hours yields the weighted average. Discretionary bonuses are excluded;
/// premiums (statutory penalties) are not part of Differentials and never enter here.
///
/// FixedHours entries with Punch.CountsTowardRegularRate = true contribute their hours to the
/// denominator and their pay (at minimumWage) to the numerator — matching how PaySummarizer
/// values these entries. Entries with the flag false (the default, e.g. paid leave) are excluded
/// entirely: they are not "hours worked" and must not dilute or inflate the rate.
/// </summary>
public static class RegularRateCalculator
{
    public static RegularRateResult Calculate(Workweek week, decimal minimumWage)
    {
        decimal straight = 0, hours = 0, differentials = 0, bonuses = 0, fixedHoursEarnings = 0;

        foreach (var day in week.Days)
        {
            foreach (var shift in day.Shifts)
            {
                foreach (var pair in shift.PunchPairs.Where(p => !p.IsMissingPunch))
                {
                    straight += pair.TotalHours * (pair.Rate ?? 0);
                    hours += pair.TotalHours;
                }

                differentials += shift.Differentials.Sum(d => d.Amount);

                bonuses += shift.FixedEntries
                    .Where(e => e.Kind == PunchKind.FixedDollar && e.BonusKind == BonusKind.NonDiscretionary)
                    .Sum(e => e.Amount ?? 0);

                foreach (var entry in shift.FixedEntries.Where(
                    e => e.Kind == PunchKind.FixedHours && e.CountsTowardRegularRate))
                {
                    var entryHours = entry.Hours ?? 0;
                    hours += entryHours;
                    fixedHoursEarnings += entryHours * minimumWage;
                }
            }
        }

        var rate = hours > 0 ? (straight + fixedHoursEarnings + bonuses + differentials) / hours : 0;

        return new RegularRateResult
        {
            StraightTimeEarnings = straight,
            FixedHoursEarnings = fixedHoursEarnings,
            NonDiscretionaryBonuses = bonuses,
            Differentials = differentials,
            TotalHours = hours,
            RegularRate = rate,
        };
    }
}
