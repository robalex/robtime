using NodaTime;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

public record WeekRetroAdjustment(LocalDate WeekStart, decimal AllocatedBonus, decimal AdditionalOvertimePremium);

public record RetroBonusResult
{
    public decimal AdditionalOvertimePremium { get; init; }
    public IReadOnlyList<WeekRetroAdjustment> PerWeek { get; init; } = [];
}

/// <summary>
/// FLSA retroactive recalculation for a non-discretionary bonus that covers multiple workweeks
/// (29 CFR §778.209).  The bonus is apportioned across the covered weeks in proportion to hours
/// worked; in each week it raises the regular rate, so the employer owes additional overtime
/// premium (0.5× for OT hours, 1.0× for doubletime hours) on the rate increase for hours already
/// worked.  Pure and deterministic — the persistence layer's recompute job simply calls this.
/// </summary>
public static class RetroactiveBonusRecalculator
{
    public static RetroBonusResult Recalculate(
        decimal bonusAmount, IReadOnlyList<Workweek> coveredWeeks, IOvertimeRule overtimeRule, decimal minimumWage)
    {
        var totalHours = coveredWeeks.Sum(w => w.TotalHours);
        var perWeek = new List<WeekRetroAdjustment>(coveredWeeks.Count);
        decimal totalAdditional = 0;

        foreach (var week in coveredWeeks)
        {
            var baseRate = RegularRateCalculator.Calculate(week, minimumWage);
            if (baseRate.TotalHours <= 0 || totalHours <= 0)
            {
                perWeek.Add(new WeekRetroAdjustment(week.StartDate, 0, 0));
                continue;
            }

            var allocated = bonusAmount * (baseRate.TotalHours / totalHours);
            var raisedRate =
                (baseRate.StraightTimeEarnings + baseRate.FixedHoursEarnings + baseRate.NonDiscretionaryBonuses
                    + baseRate.Differentials + allocated)
                / baseRate.TotalHours;
            var rateIncrease = raisedRate - baseRate.RegularRate;

            var alloc = overtimeRule.Allocate(week);
            var additional = alloc.OvertimeHours * 0.5m * rateIncrease
                           + alloc.DoubletimeHours * 1.0m * rateIncrease;

            totalAdditional += additional;
            perWeek.Add(new WeekRetroAdjustment(week.StartDate, allocated, additional));
        }

        return new RetroBonusResult { AdditionalOvertimePremium = totalAdditional, PerWeek = perWeek };
    }
}
