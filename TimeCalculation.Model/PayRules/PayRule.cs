using NodaTime;

namespace TimeCalculation.Model.PayRules;

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

    // Breaks & lunches — expected durations used by Stage 2 to classify
    // mid-shift Out→In gaps as Break or Lunch (nearest length wins)
    public int ExpectedBreakLengthMinutes { get; set; } = 15;
    public int ExpectedLunchLengthMinutes { get; set; } = 30;

    // Rounding
    public RoundingRule RoundingRule { get; set; } = new RoundingRule();

    // Shift dating
    public ShiftDateStrategy ShiftDateStrategy { get; set; } = ShiftDateStrategy.FirstPunchLocalDate;

    // State-specific premium rule codes active for this client (resolved via PremiumRegistry)
    public IReadOnlySet<string> ActivePremiumCodes { get; set; } = new HashSet<string>();

    // Workweek definition (FLSA-required consistent 168-hr window)
    public IsoDayOfWeek WorkweekStartDay { get; set; } = IsoDayOfWeek.Sunday;

    // Overtime
    public OvertimeRule OvertimeRule { get; set; } = new OvertimeRule();
}
