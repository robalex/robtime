using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 8b — Consecutive-range differential qualification.
/// Companion to DifferentialApplier (Stage 8): that stage attaches a candidate AppliedDifferential
/// to each qualifying shift, but a ConsecutiveDayRange rule with DifferentialRule.MinHoursInRange
/// can only be judged once a whole range occurrence is visible — and a range (e.g. Thursday..Tuesday)
/// is independent of the payroll workweek and can straddle it, so this runs on the full shift list,
/// not on grouped weeks.
///
/// Each shift is anchored to the occurrence of the rule's range it falls in (the most-recent
/// range-start weekday on or before Shift.ShiftDate), the same way WorkweekGrouper anchors a day to
/// its week — but keyed on the rule's own DayOfWeekRangeStart rather than PayRule.WorkweekStartDay.
/// Within each occurrence the rule's qualifying hours are summed; if the occurrence falls short, that
/// rule's AppliedDifferential is stripped from every shift in it, so it feeds neither the regular
/// rate (Stage 11) nor the itemized line items (Stage 13). Occurrences are judged independently, so
/// a multi-week pay period resets the counter each range occurrence.
/// </summary>
public static class RangeDifferentialQualifier
{
    public static IReadOnlyList<Shift> Execute(IReadOnlyList<Shift> shifts, PipelineContext ctx)
    {
        var rules = ctx.DifferentialRules
            .Where(r => r.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange && r.MinHoursInRange > 0)
            .ToList();

        if (rules.Count == 0)
        {
            return shifts;
        }

        // For each rule, the set of range-occurrence anchors whose summed qualifying hours fall short.
        var failingAnchorsByCode = new Dictionary<string, HashSet<LocalDate>>();
        var rangeStartByCode = new Dictionary<string, IsoDayOfWeek>();

        foreach (var rule in rules)
        {
            rangeStartByCode[rule.Code] = rule.DayOfWeekRangeStart;

            var failingAnchors = shifts
                .SelectMany(s => s.Differentials
                    .Where(d => d.Code == rule.Code)
                    .Select(d => (Anchor: DifferentialApplier.OccurrenceAnchor(s.ShiftDate, rule.DayOfWeekRangeStart), d.Hours)))
                .GroupBy(x => x.Anchor)
                .Where(g => g.Sum(x => x.Hours) < rule.MinHoursInRange)
                .Select(g => g.Key)
                .ToHashSet();

            if (failingAnchors.Count > 0) failingAnchorsByCode[rule.Code] = failingAnchors;
        }

        if (failingAnchorsByCode.Count == 0)
        {
            return shifts;
        }

        return shifts.Select(s => StripFailing(s, failingAnchorsByCode, rangeStartByCode)).ToList();
    }

    private static Shift StripFailing(
        Shift shift,
        Dictionary<string, HashSet<LocalDate>> failingAnchorsByCode,
        Dictionary<string, IsoDayOfWeek> rangeStartByCode)
    {
        bool Fails(AppliedDifferential d) =>
            failingAnchorsByCode.TryGetValue(d.Code, out var anchors)
            && anchors.Contains(DifferentialApplier.OccurrenceAnchor(shift.ShiftDate, rangeStartByCode[d.Code]));

        if (!shift.Differentials.Any(Fails)) return shift;

        return shift with { Differentials = shift.Differentials.Where(d => !Fails(d)).ToList() };
    }
}
