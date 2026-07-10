using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

/// <summary>
/// Stage 13 — Summarize.  Turns a workweek's computed pieces (regular rate, overtime allocation,
/// and its premium-enriched shifts) into itemized pay lines.
///
/// Composition (FLSA premium view, no double-counting):
///   Regular          = straight-time earnings (Σ hours × actual rate)
///   + Differential(s)= each AppliedDifferential
///   + Bonus(es)      = each FixedDollar entry (both bonus kinds are paid)
///   + FixedHours     = flat-hour entries, valued at minimum wage (simplification)
///   + OvertimePremium= OT/DT half/full-time uplift, sized by the regular rate
///   + Premium(s)     = meal/rest statutory penalties actually paid
/// </summary>
public static class PaySummarizer
{
    public static WorkweekPay Summarize(
        Workweek week,
        IReadOnlyList<Shift> premiumEnrichedShifts,
        RegularRateResult regularRate,
        OvertimeResult overtime,
        decimal minimumWage)
    {
        var lines = new List<PayLineItem>();

        if (regularRate.TotalHours > 0)
        {
            lines.Add(new PayLineItem
            {
                Type = PayLineType.Regular,
                Description = "Straight-time earnings",
                Hours = regularRate.TotalHours,
                Amount = regularRate.StraightTimeEarnings,
            });
        }

        foreach (var shift in premiumEnrichedShifts)
        {
            foreach (var diff in shift.Differentials)
            {
                lines.Add(new PayLineItem
                {
                    Type = PayLineType.Differential,
                    Description = $"Differential {diff.Code}",
                    Hours = diff.Hours,
                    Amount = diff.Amount,
                });
            }

            foreach (var entry in shift.FixedEntries)
            {
                if (entry.Kind == PunchKind.FixedDollar)
                {
                    lines.Add(new PayLineItem
                    {
                        Type = PayLineType.Bonus,
                        Description = $"Bonus ({entry.BonusKind})",
                        Amount = entry.Amount ?? 0,
                    });
                }
                else if (entry.Kind == PunchKind.FixedHours)
                {
                    var hrs = entry.Hours ?? 0;
                    lines.Add(new PayLineItem
                    {
                        Type = PayLineType.FixedHours,
                        Description = "Fixed hours",
                        Hours = hrs,
                        Amount = hrs * minimumWage,
                    });
                }
            }

            foreach (var premium in shift.Premiums.Where(p => p.IsPaid))
            {
                lines.Add(new PayLineItem
                {
                    Type = PayLineType.Premium,
                    Description = $"Premium {premium.Code}",
                    Hours = premium.Hours,
                    Amount = premium.Amount,
                });
            }
        }

        if (overtime.OvertimePremium > 0)
        {
            lines.Add(new PayLineItem
            {
                Type = PayLineType.OvertimePremium,
                Description = "Overtime premium",
                Hours = overtime.Allocation.OvertimeHours + overtime.Allocation.DoubletimeHours,
                Amount = overtime.OvertimePremium,
            });
        }

        return new WorkweekPay
        {
            WeekStart = week.StartDate,
            RegularRate = regularRate.RegularRate,
            RegularHours = overtime.Allocation.RegularHours,
            OvertimeHours = overtime.Allocation.OvertimeHours,
            DoubletimeHours = overtime.Allocation.DoubletimeHours,
            LineItems = lines,
        };
    }
}
