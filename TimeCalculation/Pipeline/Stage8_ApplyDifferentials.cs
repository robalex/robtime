using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 8 — Time-based differentials.
/// For each shift and each active DifferentialRule, intersects the shift's worked time with the
/// rule's day/holiday filter and time-of-day window, apportioning qualifying hours.  If the
/// qualifying hours meet the rule's minimum, an AppliedDifferential is attached to the shift.
///
/// Amount:
///   FlatPerHour → qualifyingHours × AdjustmentValue
///   Multiplier  → Σ (segmentHours × pairRate × AdjustmentValue)   (rate-weighted per pair)
///   FixedBonus  → AdjustmentValue once, when the shift qualifies
/// </summary>
public static class Stage8_ApplyDifferentials
{
    private const int SecondsPerDay = 86_400;

    public static IReadOnlyList<Shift> Execute(IReadOnlyList<Shift> shifts, PipelineContext ctx)
    {
        if (ctx.DifferentialRules.Count == 0) return shifts;
        return shifts.Select(s => Apply(s, ctx)).ToList();
    }

    private static Shift Apply(Shift shift, PipelineContext ctx)
    {
        var applied = new List<AppliedDifferential>();

        foreach (var rule in ctx.DifferentialRules)
        {
            decimal qualifyingHours = 0;
            decimal perHourAmount = 0;

            foreach (var pair in shift.PunchPairs.Where(p => !p.IsMissingPunch))
            {
                var rate = pair.Rate ?? 0;
                foreach (var (date, startSec, endSec) in DaySegments(
                    pair.InPunch!.EffectiveTime, pair.OutPunch!.EffectiveTime, ctx.EmployeeTimeZone))
                {
                    if (!RuleAppliesOn(rule, date, ctx)) continue;

                    int overlapSec = WindowOverlapSeconds(startSec, endSec, rule);
                    if (overlapSec <= 0) continue;

                    var hrs = (decimal)overlapSec / 3600m;
                    qualifyingHours += hrs;
                    if (rule.AdjustmentType == DifferentialAdjustmentType.FlatPerHour)
                        perHourAmount += hrs * rule.AdjustmentValue;
                    else if (rule.AdjustmentType == DifferentialAdjustmentType.Multiplier)
                        perHourAmount += hrs * rate * rule.AdjustmentValue;
                }
            }

            if (qualifyingHours < rule.MinHoursInWindow || qualifyingHours <= 0)
                continue;

            var amount = rule.AdjustmentType == DifferentialAdjustmentType.FixedBonus
                ? rule.AdjustmentValue
                : perHourAmount;

            applied.Add(new AppliedDifferential
            {
                Code = rule.Code,
                Hours = qualifyingHours,
                Amount = amount,
                AdjustmentType = rule.AdjustmentType,
            });
        }

        return applied.Count == 0 ? shift : shift with { Differentials = applied };
    }

    private static bool RuleAppliesOn(DifferentialRule rule, LocalDate date, PipelineContext ctx)
    {
        if (rule.DaysOfWeek.Count > 0 && !rule.DaysOfWeek.Contains(date.DayOfWeek))
            return false;
        if (rule.HolidaysOnly && ctx.HolidayCalendar?.IsHoliday(date) != true)
            return false;
        return true;
    }

    private static int WindowOverlapSeconds(int aStart, int aEnd, DifferentialRule rule)
    {
        if (rule.IsAllDay) return aEnd - aStart;

        int ws = SecondsOf(rule.WindowStart);
        int we = SecondsOf(rule.WindowEnd);

        return ws < we
            ? Overlap(aStart, aEnd, ws, we)                                   // same-day window
            : Overlap(aStart, aEnd, ws, SecondsPerDay) + Overlap(aStart, aEnd, 0, we);  // wraps midnight
    }

    private static int Overlap(int aStart, int aEnd, int bStart, int bEnd)
        => Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));

    private static int SecondsOf(LocalTime t) => t.Hour * 3600 + t.Minute * 60 + t.Second;

    // Splits a worked interval into per-local-date segments, each expressed as [startSec, endSec)
    // seconds-of-day.  A segment ending at the next local midnight is reported as SecondsPerDay.
    private static IEnumerable<(LocalDate Date, int StartSec, int EndSec)> DaySegments(
        Instant start, Instant end, DateTimeZone zone)
    {
        var current = start;
        while (current < end)
        {
            var zdt = current.InZone(zone);
            var date = zdt.Date;
            var nextMidnight = date.PlusDays(1).AtMidnight().InZoneLeniently(zone).ToInstant();
            var segEnd = end < nextMidnight ? end : nextMidnight;

            int startSec = SecondsOf(zdt.TimeOfDay);
            int endSec = segEnd == nextMidnight ? SecondsPerDay : SecondsOf(segEnd.InZone(zone).TimeOfDay);

            yield return (date, startSec, endSec);
            current = nextMidnight;
        }
    }
}
