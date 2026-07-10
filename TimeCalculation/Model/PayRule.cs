using NodaTime;

namespace TimeCalculation.Model;

public class PayRule
{
    public int Id { get; set; }
    public int Version { get; set; }
    public int ClientId { get; set; }

    // Punch inference
    public decimal PunchPairResetHours { get; set; } = 15;

    // Pairing
    public decimal MaxShiftLengthHours { get; set; } = 15;
    public decimal DistanceBetweenShiftsHours { get; set; } = 6;

    // Rounding
    public RoundingStrategy RoundingStrategy { get; set; } = RoundingStrategy.None;
    public int RoundingIntervalMinutes { get; set; } = 15;
    public int RoundingGraceMinutes { get; set; } = 7;

    // Shift dating
    public ShiftDateStrategy ShiftDateStrategy { get; set; } = ShiftDateStrategy.FirstPunchLocalDate;

    // Workweek definition (FLSA-required consistent 168-hr window)
    public IsoDayOfWeek WorkweekStartDay { get; set; } = IsoDayOfWeek.Sunday;

    // Overtime
    public decimal WeeklyOvertimeThresholdHours { get; set; } = 40;
    public bool HasDailyOvertime { get; set; }
    public decimal DailyOvertimeThresholdHours { get; set; } = 8;
    public decimal DailyDoubletimeThresholdHours { get; set; } = 12;
    public bool HasSeventhDayRule { get; set; }
}
