using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.ShiftBuilding;

/// <summary>
/// Stage 6 — Shift date assignment.
/// Sets Shift.ShiftDate according to PayRule.ShiftDateStrategy:
///   FirstPunchLocalDate    — date of the first In punch in the employee's timezone (default).
///   LastPunchLocalDate     — date of the last Out punch.
///   MajorityHoursLocalDate — date on which the majority of worked hours fall.
///   SplitAtMidnight        — split a midnight-crossing shift into one shift per day it touches,
///                            each dated to its own day (see MidnightShiftSplitter).
///
/// Every other strategy is 1 shift in → 1 shift out; SplitAtMidnight can return more shifts than it
/// received, which is why the stage maps with SelectMany.
/// </summary>
public static class ShiftDater
{
    public static IReadOnlyList<Shift> AssignDatesToShifts(IReadOnlyList<Shift> shifts, PipelineContext ctx)
        => shifts.SelectMany(s => AssignDate(s, ctx)).ToList();

    private static IEnumerable<Shift> AssignDate(Shift shift, PipelineContext ctx)
    {
        var firstPair = shift.PunchPairs.OrderBy(AnchorTime).FirstOrDefault();
        if (firstPair is null) return [shift];

        var firstTime = AnchorTime(firstPair);
        var rule = ctx.GetRuleAt(firstTime);

        if (rule.ShiftDateStrategy == ShiftDateStrategy.SplitAtMidnight)
        {
            return MidnightShiftSplitter.Split(shift, ctx);
        }

        var date = rule.ShiftDateStrategy switch
        {
            ShiftDateStrategy.LastPunchLocalDate    => GetLastPunchDate(shift, ctx),
            ShiftDateStrategy.MajorityHoursLocalDate => GetMajorityHoursDate(shift, ctx),
            _                                       => firstTime.InZone(ctx.EmployeeTimeZone).Date,
        };

        return [shift with { ShiftDate = date }];
    }

    // A pair's own anchor time: its In, or its Out when it's an orphan Out with no In.
    private static Instant AnchorTime(PunchPair pair) => pair.InPunch?.EffectiveTime ?? pair.OutPunch!.EffectiveTime;

    private static LocalDate GetLastPunchDate(Shift shift, PipelineContext ctx)
    {
        var lastPunch = shift.PunchPairs
            .OrderByDescending(p => p.OutPunch?.EffectiveTime ?? p.InPunch!.EffectiveTime)
            .First();

        var lastTime = lastPunch.OutPunch?.EffectiveTime ?? lastPunch.InPunch!.EffectiveTime;
        return lastTime.InZone(ctx.EmployeeTimeZone).Date;
    }

    private static LocalDate GetMajorityHoursDate(Shift shift, PipelineContext ctx)
    {
        var hoursByDate = new Dictionary<LocalDate, decimal>();

        // Only complete pairs (both In and Out) have hours to apportion; an orphan pair (only one
        // of the two present, e.g. IsMissingPunch) contributes no worked time either way.
        foreach (var pair in shift.PunchPairs.Where(p => !p.IsMissingPunch))
        {
            foreach (var (date, hours) in ApportionByDate(
                pair.InPunch!.EffectiveTime, pair.OutPunch!.EffectiveTime, ctx))
            {
                hoursByDate[date] = hoursByDate.GetValueOrDefault(date) + hours;
            }
        }

        if (hoursByDate.Count == 0)
            return AnchorTime(shift.PunchPairs.First()).InZone(ctx.EmployeeTimeZone).Date;

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
