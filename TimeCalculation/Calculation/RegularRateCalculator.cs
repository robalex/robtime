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
/// </summary>
public static class RegularRateCalculator
{
    public static RegularRateResult Calculate(Workweek week)
    {
        decimal straight = 0, hours = 0, differentials = 0, bonuses = 0;

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
            }
        }

        var rate = hours > 0 ? (straight + bonuses + differentials) / hours : 0;

        return new RegularRateResult
        {
            StraightTimeEarnings = straight,
            NonDiscretionaryBonuses = bonuses,
            Differentials = differentials,
            TotalHours = hours,
            RegularRate = rate,
        };
    }
}
