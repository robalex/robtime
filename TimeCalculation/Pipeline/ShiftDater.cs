using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 6 — Shift date assignment.
/// Sets Shift.ShiftDate according to PayRule.ShiftDateStrategy:
///   FirstPunchLocalDate  — date of the first In punch in the employee's timezone (default).
///   MajorityHoursLocalDate — date on which the majority of worked hours fall.
/// </summary>
public static class ShiftDater
{
    public static IReadOnlyList<Shift> AssignDatesToShifts(IReadOnlyList<Shift> shifts, PipelineContext ctx)
        => shifts.Select(s => AssignDate(s, ctx)).ToList();

    private static Shift AssignDate(Shift shift, PipelineContext ctx)
    {
        var firstIn = shift.PunchPairs
            .OrderBy(p => p.InPunch.EffectiveTime)
            .FirstOrDefault()?.InPunch;

        if (firstIn is null) return shift;

        var rule = ctx.GetRuleAt(firstIn.EffectiveTime);

        var date = rule.ShiftDateStrategy switch
        {
            ShiftDateStrategy.LastPunchLocalDate    => GetLastPunchDate(shift, ctx),
            ShiftDateStrategy.MajorityHoursLocalDate => GetMajorityHoursDate(shift, ctx),
            _                                       => firstIn.EffectiveTime.InZone(ctx.EmployeeTimeZone).Date,
        };

        return shift with { ShiftDate = date };
    }

    private static LocalDate GetLastPunchDate(Shift shift, PipelineContext ctx)
    {
        var lastPunch = shift.PunchPairs
            .OrderByDescending(p => p.OutPunch?.EffectiveTime ?? p.InPunch.EffectiveTime)
            .First();

        var lastTime = lastPunch.OutPunch?.EffectiveTime ?? lastPunch.InPunch.EffectiveTime;
        return lastTime.InZone(ctx.EmployeeTimeZone).Date;
    }

    private static LocalDate GetMajorityHoursDate(Shift shift, PipelineContext ctx)
    {
        var hoursByDate = new Dictionary<LocalDate, decimal>();

        foreach (var pair in shift.PunchPairs.Where(p => p.OutPunch is not null))
        {
            foreach (var (date, hours) in ApportionByDate(
                pair.InPunch.EffectiveTime, pair.OutPunch!.EffectiveTime, ctx))
            {
                hoursByDate[date] = hoursByDate.GetValueOrDefault(date) + hours;
            }
        }

        if (hoursByDate.Count == 0)
            return shift.PunchPairs.First().InPunch.EffectiveTime.InZone(ctx.EmployeeTimeZone).Date;

        return hoursByDate.MaxBy(kv => kv.Value).Key;
    }

    // Yields (date, hours) pairs apportioning a time range across calendar-day boundaries.
    private static IEnumerable<(LocalDate Date, decimal Hours)> ApportionByDate(
        Instant start, Instant end, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        var current = start;

        while (current < end)
        {
            var currentDate = current.InZone(zone).Date;
            var nextMidnight = currentDate.PlusDays(1).AtMidnight().InZoneLeniently(zone).ToInstant();
            var segEnd = end < nextMidnight ? end : nextMidnight;

            yield return (currentDate, (decimal)(segEnd - current).TotalHours);

            current = nextMidnight;
        }
    }
}
