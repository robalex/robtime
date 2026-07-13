using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;

namespace TimeCalculation.Calculation;

/// <summary>
/// Stage 13 — Summarize.  Turns a workweek's computed pieces (regular rate, overtime allocation,
/// and its premium-enriched shifts) into a per-shift pay breakdown, so a UI can show why each
/// piece of a workday was paid the way it was.
///
/// Composition per shift (FLSA premium view, no double-counting):
///   Regular          = one line per punch pair: that pair's hours × its own rate
///   + Differential(s)= each AppliedDifferential
///   + Bonus(es)      = each FixedDollar entry (both bonus kinds are paid)
///   + FixedHours     = flat-hour entries, valued at minimum wage (simplification)
///   + OvertimePremium= this pair's share of the week's OT/DT premium (see attribution below)
///   + Premium(s)     = meal/rest statutory penalties actually paid
///
/// Overtime attribution: OvertimeAllocation gives week-level Regular/Overtime/Doubletime hour
/// totals, not a per-shift breakdown — neither federal (a pure weekly total) nor California's
/// weekly-remainder bucket names which specific hours are the "extra" ones. This adopts the common
/// payroll convention that hours accrue toward overtime in the order worked: walking every punch
/// pair across the whole week in chronological order, the first RegularHours worth of hours are
/// regular, the next OvertimeHours worth are overtime, and anything remaining is doubletime. A
/// pair straddling a bucket boundary is split proportionally. This always sums back to exactly
/// OvertimeResult.OvertimePremium — it only decides WHICH shift/pair the premium is attributed to,
/// never how much is owed in total.
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
        var remainingRegular = overtime.Allocation.RegularHours;
        var remainingOvertime = overtime.Allocation.OvertimeHours;
        var remainingDoubletime = overtime.Allocation.DoubletimeHours;

        var shiftPays = new List<ShiftPay>(premiumEnrichedShifts.Count);

        // premiumEnrichedShifts arrives in chronological order (built by ShiftBuilder, carried
        // through unchanged by every stage since), and each shift's own PunchPairs are chronological
        // too — so a single pass here is the whole week's chronological pair sequence, letting the
        // OT/DT buckets above be consumed in the right order without a separate sort.
        foreach (var shift in premiumEnrichedShifts)
        {
            var lines = new List<PayLineItem>();

            foreach (var pair in shift.PunchPairs.Where(p => !p.IsMissingPunch))
            {
                var hours = pair.TotalHours;
                var rate = pair.Rate ?? 0;

                lines.Add(new PayLineItem
                {
                    Type = PayLineType.Regular,
                    Description = "Straight-time earnings",
                    Hours = hours,
                    Amount = hours * rate,
                    ShiftDate = shift.ShiftDate,
                    AnchorPunchId = shift.AnchorPunchId,
                });

                var regularPortion = Math.Min(hours, remainingRegular);
                remainingRegular -= regularPortion;
                hours -= regularPortion;

                var otPortion = Math.Min(hours, remainingOvertime);
                remainingOvertime -= otPortion;
                hours -= otPortion;

                var dtPortion = Math.Min(hours, remainingDoubletime);
                remainingDoubletime -= dtPortion;

                var premiumAmount = otPortion * 0.5m * regularRate.RegularRate
                                   + dtPortion * 1.0m * regularRate.RegularRate;
                if (premiumAmount > 0)
                {
                    lines.Add(new PayLineItem
                    {
                        Type = PayLineType.OvertimePremium,
                        Description = "Overtime premium",
                        Hours = otPortion + dtPortion,
                        Amount = premiumAmount,
                        ShiftDate = shift.ShiftDate,
                        AnchorPunchId = shift.AnchorPunchId,
                    });
                }
            }

            foreach (var diff in shift.Differentials)
            {
                lines.Add(new PayLineItem
                {
                    Type = PayLineType.Differential,
                    Description = $"Differential {diff.Code}",
                    Hours = diff.Hours,
                    Amount = diff.Amount,
                    ShiftDate = shift.ShiftDate,
                    AnchorPunchId = shift.AnchorPunchId,
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
                        ShiftDate = shift.ShiftDate,
                        AnchorPunchId = shift.AnchorPunchId,
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
                        ShiftDate = shift.ShiftDate,
                        AnchorPunchId = shift.AnchorPunchId,
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
                    ShiftDate = shift.ShiftDate,
                    AnchorPunchId = shift.AnchorPunchId,
                });
            }

            if (lines.Count > 0)
            {
                shiftPays.Add(new ShiftPay
                {
                    ShiftDate = shift.ShiftDate,
                    AnchorPunchId = shift.AnchorPunchId,
                    LineItems = lines,
                });
            }
        }

        return new WorkweekPay
        {
            WeekStart = week.StartDate,
            RegularRate = regularRate.RegularRate,
            RegularHours = overtime.Allocation.RegularHours,
            OvertimeHours = overtime.Allocation.OvertimeHours,
            DoubletimeHours = overtime.Allocation.DoubletimeHours,
            Shifts = shiftPays,
        };
    }
}
