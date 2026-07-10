using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 9 — Group into Days.
/// Buckets shifts by their assigned ShiftDate (set in Stage 6) into WorkDays, ordered
/// ascending.  WorkDays are the unit for daily-overtime and consecutive-day rules.
/// A date with no shifts produces no WorkDay.
/// </summary>
public static class Stage9_GroupIntoDays
{
    public static IReadOnlyList<WorkDay> Execute(IReadOnlyList<Shift> shifts, PipelineContext ctx)
        => shifts
            .GroupBy(s => s.ShiftDate)
            .OrderBy(g => g.Key)
            .Select(g => new WorkDay { Date = g.Key, Shifts = g.ToList() })
            .ToList();
}
