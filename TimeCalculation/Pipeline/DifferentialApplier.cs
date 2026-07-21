using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 8 — Time-based differentials.
/// For each shift and each active DifferentialRule, intersects the shift's worked time with the
/// rule's day schedule and time-of-day window, apportioning qualifying hours.  If the qualifying
/// hours meet the rule's minimum, an AppliedDifferential is attached to the shift.
///
/// The window is interpreted differently by day mode:
///   • ConsecutiveDayRange — WindowStart/WindowEnd bound ONE continuous span per range occurrence,
///     from WindowStart on the range's first day to WindowEnd on its last day (e.g. noon Thursday
///     to 5pm Monday, with the interior days fully covered).
///   • every other mode — WindowStart/WindowEnd is a per-day window applied independently on each
///     active day (e.g. noon–5pm on each listed weekday).
///
/// Amount:
///   FlatPerHour → qualifyingHours × AdjustmentValue
///   Multiplier  → Σ (segmentHours × pairRate × AdjustmentValue)   (rate-weighted per pair)
///   FixedBonus  → AdjustmentValue once, when the shift qualifies
///
/// Stacking: qualifying differentials stack additively by default.  Rules that share a non-empty
/// DifferentialRule.ExclusivityGroup are mutually exclusive — only the highest-amount one in the
/// group applies to the shift (ties broken by Code for determinism).
///
/// A shift with any missing punch (Shift.HasMissingPunches) is skipped entirely — an orphan pair
/// has no real worked interval to intersect against a rule's time-of-day window.
/// </summary>
public static class DifferentialApplier
{
    public static IReadOnlyList<Shift> Execute(IReadOnlyList<Shift> shifts, PipelineContext ctx)
    {
        if (ctx.DifferentialRules.Count == 0) return shifts;
        return shifts.Select(s => Apply(s, ctx)).ToList();
    }

    private static Shift Apply(Shift shift, PipelineContext ctx)
    {
        if (shift.HasMissingPunches)
        {
            return shift;
        }

        var candidates = new List<(DifferentialRule Rule, AppliedDifferential Applied)>();

        foreach (var rule in ctx.DifferentialRules)
        {
            decimal qualifyingHours = 0;
            decimal perHourAmount = 0;

            foreach (var pair in shift.PunchPairs)
            {
                var rate = pair.Rate ?? 0;
                var hrs = QualifyingHoursForPair(rule, pair, ctx);
                if (hrs <= 0) continue;

                qualifyingHours += hrs;
                if (rule.AdjustmentType == DifferentialAdjustmentType.FlatPerHour)
                    perHourAmount += hrs * rule.AdjustmentValue;
                else if (rule.AdjustmentType == DifferentialAdjustmentType.Multiplier)
                    perHourAmount += hrs * rate * rule.AdjustmentValue;
            }

            if (qualifyingHours < rule.MinHoursInWindow || qualifyingHours <= 0)
                continue;

            var amount = rule.AdjustmentType == DifferentialAdjustmentType.FixedBonus
                ? rule.AdjustmentValue
                : perHourAmount;

            candidates.Add((rule, new AppliedDifferential
            {
                Code = rule.Code,
                Hours = qualifyingHours,
                Amount = amount,
                AdjustmentType = rule.AdjustmentType,
                AdjustmentValue = rule.AdjustmentValue,
            }));
        }

        var applied = ResolveExclusivity(candidates);
        return applied.Count == 0 ? shift : shift with { Differentials = applied };
    }

    // Ungrouped differentials all apply; within each exclusivity group only the highest-amount
    // one survives (ties broken by Code). Original evaluation order is preserved.
    private static List<AppliedDifferential> ResolveExclusivity(
        List<(DifferentialRule Rule, AppliedDifferential Applied)> candidates)
    {
        var winningGroupCodes = candidates
            .Where(c => !string.IsNullOrEmpty(c.Rule.ExclusivityGroup))
            .GroupBy(c => c.Rule.ExclusivityGroup)
            .Select(g => g
                .OrderByDescending(c => c.Applied.Amount)
                .ThenBy(c => c.Rule.Code, StringComparer.Ordinal)
                .First().Rule.Code)
            .ToHashSet();

        var result = new List<AppliedDifferential>();
        foreach (var (rule, applied) in candidates)
        {
            bool grouped = !string.IsNullOrEmpty(rule.ExclusivityGroup);
            if (!grouped || winningGroupCodes.Contains(rule.Code))
                result.Add(applied);
        }
        return result;
    }

    // A single DayScheduleMode decides which days a rule is active on — the modes are mutually
    // exclusive, so this is a straight switch rather than a chain of ANDed filters.
    private static bool RuleAppliesOn(DifferentialRule rule, LocalDate date, PipelineContext ctx)
        => rule.DayScheduleMode switch
        {
            DayScheduleMode.EveryDay => true,
            DayScheduleMode.DaysOfWeek => rule.DaysOfWeek.Contains(date.DayOfWeek),
            DayScheduleMode.ConsecutiveDayRange =>
                IsInDayOfWeekRange(date.DayOfWeek, rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd),
            DayScheduleMode.SpecificDates => rule.SpecificDates.Contains(date),
            DayScheduleMode.Holidays => ctx.HolidayCalendar?.IsHoliday(date) == true,
            _ => true,
        };

    // Inclusive day-of-week range that may wrap past Sunday (e.g. Thursday..Tuesday), the same
    // way WindowOverlapSeconds wraps WindowStart/WindowEnd past midnight.
    internal static bool IsInDayOfWeekRange(IsoDayOfWeek day, IsoDayOfWeek start, IsoDayOfWeek end)
    {
        return start <= end
            ? day >= start && day <= end
            : day >= start || day <= end;
    }

    // Qualifying hours a single pair contributes to a rule. ConsecutiveDayRange treats the window
    // as one continuous multi-day span (see ContinuousRangeQualifyingHours); every other mode
    // applies the window per active day.
    private static decimal QualifyingHoursForPair(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
        => rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange
            ? ContinuousRangeQualifyingHours(rule, pair, ctx)
            : PerDayQualifyingHours(rule, pair, ctx);

    private static decimal PerDayQualifyingHours(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        decimal hours = 0;
        foreach (var segment in SplitWorkedIntervalIntoDaySegments(pair, ctx.EmployeeTimeZone))
        {
            if (!RuleAppliesOn(rule, segment.Date, ctx)) continue;

            int overlapSec = WindowOverlapSeconds(segment.StartSec, segment.EndSec, rule);
            if (overlapSec > 0) hours += (decimal)overlapSec / 3600m;
        }
        return hours;
    }

    // The rule's window bounds one continuous span per range occurrence: WindowStart on the range's
    // first day (its start weekday) through WindowEnd on its last day. Interior days are fully
    // covered. A pair (at most one shift long) can reach the occurrence its start date anchors to,
    // or — when that date sits in the gap before the range begins — the next one, so both are summed.
    private static decimal ContinuousRangeQualifyingHours(DifferentialRule rule, PunchPair pair, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        var inInstant = pair.InPunch!.EffectiveTime;
        var outInstant = pair.OutPunch!.EffectiveTime;

        var baseAnchor = OccurrenceAnchor(inInstant.InZone(zone).Date, rule.DayOfWeekRangeStart);
        int rangeLenDays = DayOfWeekRangeLength(rule.DayOfWeekRangeStart, rule.DayOfWeekRangeEnd);

        decimal hours = 0;
        foreach (var anchor in new[] { baseAnchor, baseAnchor.PlusDays(7) })
        {
            var (spanStart, spanEnd) = OccurrenceSpan(rule, anchor, rangeLenDays, zone);
            var overlapStart = inInstant > spanStart ? inInstant : spanStart;
            var overlapEnd = outInstant < spanEnd ? outInstant : spanEnd;
            if (overlapEnd > overlapStart)
                hours += (decimal)(overlapEnd - overlapStart).TotalHours;
        }
        return hours;
    }

    // The [start, end) instants of the range occurrence anchored on `anchor` (a range-start weekday).
    // All-day (WindowStart == WindowEnd) covers whole days: midnight of the first day to midnight
    // after the last. Otherwise WindowStart on the first day to WindowEnd on the last.
    private static (Instant Start, Instant End) OccurrenceSpan(
        DifferentialRule rule, LocalDate anchor, int rangeLenDays, DateTimeZone zone)
    {
        if (rule.IsAllDay)
        {
            return (
                anchor.AtMidnight().InZoneLeniently(zone).ToInstant(),
                anchor.PlusDays(rangeLenDays).AtMidnight().InZoneLeniently(zone).ToInstant());
        }

        return (
            anchor.At(rule.WindowStart).InZoneLeniently(zone).ToInstant(),
            anchor.PlusDays(rangeLenDays - 1).At(rule.WindowEnd).InZoneLeniently(zone).ToInstant());
    }

    private static int DayOfWeekRangeLength(IsoDayOfWeek start, IsoDayOfWeek end)
        => (((int)end - (int)start + 7) % 7) + 1;

    // Most recent date on or before `date` whose weekday is `rangeStart` — the start of the range
    // occurrence this date belongs to. Shared with RangeDifferentialQualifier so the applier and the
    // Stage 8b threshold check agree on which occurrence a shift falls in.
    internal static LocalDate OccurrenceAnchor(LocalDate date, IsoDayOfWeek rangeStart)
    {
        int diff = ((int)date.DayOfWeek - (int)rangeStart + 7) % 7;
        return date.PlusDays(-diff);
    }

    private static int WindowOverlapSeconds(int aStart, int aEnd, DifferentialRule rule)
    {
        if (rule.IsAllDay) return aEnd - aStart;

        int ws = SecondsOf(rule.WindowStart);
        int we = SecondsOf(rule.WindowEnd);

        return ws < we
            ? Overlap(aStart, aEnd, ws, we)                                   // same-day window
            : Overlap(aStart, aEnd, ws, NodaConstants.SecondsPerDay) + Overlap(aStart, aEnd, 0, we);  // wraps midnight
    }

    private static int Overlap(int aStart, int aEnd, int bStart, int bEnd)
        => Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));

    private static int SecondsOf(LocalTime t) => t.Hour * 3600 + t.Minute * 60 + t.Second;

    private static IEnumerable<DaySegment> SplitWorkedIntervalIntoDaySegments(PunchPair workedPair, DateTimeZone timeZone) 
    { 
        var current = workedPair.InPunch!.EffectiveTime;
        var end = workedPair.OutPunch!.EffectiveTime;
        while (current < end)
        {
            var zdt = current.InZone(timeZone);
            var date = zdt.Date;
            var nextMidnight = date.PlusDays(1).AtMidnight().InZoneLeniently(timeZone).ToInstant();
            var segEnd = end < nextMidnight ? end : nextMidnight;

            int startSec = SecondsOf(zdt.TimeOfDay);
            int endSec = segEnd == nextMidnight ? NodaConstants.SecondsPerDay : SecondsOf(segEnd.InZone(timeZone).TimeOfDay);

            yield return new DaySegment(date, startSec, endSec);
            current = nextMidnight;
        }
    }
}
