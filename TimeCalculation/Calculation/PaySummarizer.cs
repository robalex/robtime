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
///   + OvertimePremium= this pair's share of the week's OT/DT premium (see attribution below);
///                      split into separate OVERTIME/DOUBLETIME lines when a pair straddles both
///   + Premium(s)     = meal/rest statutory penalties actually paid
///
/// Every line carries BaseRate/Multiplier when a single one meaningfully applies, so
/// Amount == Hours × BaseRate × Multiplier and a UI never has to re-derive "why this dollar
/// figure" — see PayLineItem's doc comment for exactly what each Type populates.
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
                    BaseRate = rate,
                    Multiplier = 1.0m,
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

                // Split into separate OT/DT lines (rather than one combined line) so each has a
                // single, honest Multiplier — a pair can straddle both tiers in the same shift.
                if (otPortion > 0)
                {
                    lines.Add(new PayLineItem
                    {
                        Type = PayLineType.OvertimePremium,
                        Code = "OVERTIME",
                        Description = "Overtime premium",
                        Hours = otPortion,
                        Amount = otPortion * 0.5m * regularRate.RegularRate,
                        BaseRate = regularRate.RegularRate,
                        Multiplier = 0.5m,
                        ShiftDate = shift.ShiftDate,
                        AnchorPunchId = shift.AnchorPunchId,
                    });
                }
                if (dtPortion > 0)
                {
                    lines.Add(new PayLineItem
                    {
                        Type = PayLineType.OvertimePremium,
                        Code = "DOUBLETIME",
                        Description = "Doubletime premium",
                        Hours = dtPortion,
                        Amount = dtPortion * 1.0m * regularRate.RegularRate,
                        BaseRate = regularRate.RegularRate,
                        Multiplier = 1.0m,
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
                    Code = diff.Code,
                    Description = $"Differential {diff.Code}",
                    Hours = diff.Hours,
                    Amount = diff.Amount,
                    BaseRate = DifferentialBaseRate(diff),
                    Multiplier = DifferentialMultiplier(diff),
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
                        BaseRate = minimumWage,
                        Multiplier = 1.0m,
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
                    Code = premium.Code,
                    Description = $"Premium {premium.Code}",
                    Hours = premium.Hours,
                    Amount = premium.Amount,
                    BaseRate = premium.BaseRate,
                    Multiplier = premium.Multiplier,
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

    // FlatPerHour: the configured $/hr IS the rate — Multiplier is 1.0 by definition.
    // Multiplier-type: solved back from the known Amount/Hours/AdjustmentValue rather than
    // re-walking pairs, so it's exact even when the qualifying hours span pairs at different
    // rates in a multi-rate week (an "effective average rate" in that case).
    // FixedBonus: a flat lump sum has no per-hour rate to report.
    private static decimal? DifferentialBaseRate(AppliedDifferential diff) => diff.AdjustmentType switch
    {
        DifferentialAdjustmentType.FlatPerHour => diff.AdjustmentValue,
        DifferentialAdjustmentType.Multiplier when diff.Hours > 0 && diff.AdjustmentValue != 0
            => diff.Amount / (diff.Hours * diff.AdjustmentValue),
        _ => null,
    };

    private static decimal? DifferentialMultiplier(AppliedDifferential diff) => diff.AdjustmentType switch
    {
        DifferentialAdjustmentType.FlatPerHour => 1.0m,
        DifferentialAdjustmentType.Multiplier when diff.Hours > 0 && diff.AdjustmentValue != 0
            => diff.AdjustmentValue,
        _ => null,
    };
}
