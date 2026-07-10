using System;
using System.Collections.Generic;
using System.Text;

namespace TimeCalculation.Model.PayRules;

public class OvertimeRule
{
    public decimal WeeklyOvertimeThresholdHours { get; set; } = 40;
    public bool HasDailyOvertime { get; set; }
    public decimal DailyOvertimeThresholdHours { get; set; } = 8;
    public decimal DailyDoubletimeThresholdHours { get; set; } = 12;
    public bool HasSeventhDayRule { get; set; }
}
