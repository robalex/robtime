using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// Sums a ConsecutiveDayRange rule's already-applied qualifying hours per range occurrence across a
/// shift list — the same grouping RangeDifferentialQualifier uses to decide which occurrences fall
/// short of DifferentialRule.MinHoursInRange, extracted so the sandbox explainer can report the exact
/// summed hours and threshold for a shift's own occurrence, not just whether it failed.
/// </summary>
internal static class RangeOccurrenceHours
{
    internal static Dictionary<LocalDate, decimal> SumByOccurrenceAnchor(DifferentialRule rule, IReadOnlyList<Shift> shifts)
    {
        var sums = new Dictionary<LocalDate, decimal>();
        foreach (var shift in shifts)
        {
            foreach (var applied in shift.Differentials.Where(d => d.Code == rule.Code))
            {
                var anchor = DayOfWeekRange.OccurrenceAnchor(shift.ShiftDate, rule.DayOfWeekRangeStart);
                sums[anchor] = sums.GetValueOrDefault(anchor) + applied.Hours;
            }
        }
        return sums;
    }
}
