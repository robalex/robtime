using TimeCalculation.Model;

namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// California-style daily + weekly + 7th-consecutive-day overtime.
///
///  • Daily: hours over the daily OT threshold (8) → 1.5×; hours over the doubletime threshold (12) → 2×.
///  • 7th consecutive workday in the workweek: first 8 hours → 1.5×, hours beyond 8 → 2× (whole day is premium).
///  • Weekly: regular hours (those not already daily-OT) beyond the weekly threshold (40) → 1.5×.
///
/// Daily and seventh-day handling can each be toggled so the same class serves other daily-OT states.
/// </summary>
public class CaliforniaOvertimeRule : IOvertimeRule
{
    private readonly decimal _dailyOt;
    private readonly decimal _dailyDt;
    private readonly decimal _weekly;
    private readonly bool _applyDaily;
    private readonly bool _applySeventhDay;

    public CaliforniaOvertimeRule(
        decimal dailyOtThreshold = 8m,
        decimal dailyDtThreshold = 12m,
        decimal weeklyThreshold = 40m,
        bool applyDaily = true,
        bool applySeventhDay = true)
    {
        _dailyOt = dailyOtThreshold;
        _dailyDt = dailyDtThreshold;
        _weekly = weeklyThreshold;
        _applyDaily = applyDaily;
        _applySeventhDay = applySeventhDay;
    }

    public OvertimeAllocation Allocate(Workweek week)
    {
        decimal regular = 0, overtime = 0, doubletime = 0;

        foreach (var day in week.Days)
        {
            var hours = day.TotalHours;

            if (_applySeventhDay && day.ConsecutiveDayNumber == 7)
            {
                overtime += Math.Min(hours, _dailyOt);
                doubletime += Math.Max(0, hours - _dailyOt);
            }
            else if (_applyDaily)
            {
                regular += Math.Min(hours, _dailyOt);
                overtime += Math.Max(0, Math.Min(hours, _dailyDt) - _dailyOt);
                doubletime += Math.Max(0, hours - _dailyDt);
            }
            else
            {
                regular += hours;
            }
        }

        // Weekly overtime applies only to regular hours not already counted as daily OT.
        if (regular > _weekly)
        {
            overtime += regular - _weekly;
            regular = _weekly;
        }

        return new OvertimeAllocation
        {
            RegularHours = regular,
            OvertimeHours = overtime,
            DoubletimeHours = doubletime,
        };
    }
}
