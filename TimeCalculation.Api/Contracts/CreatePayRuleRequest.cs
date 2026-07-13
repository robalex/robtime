using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Every field but ClientId is optional — anything omitted falls back to PayRule's own default
/// (see TimeCalculation.Model.PayRules.PayRule), so this never duplicates those defaults itself.
/// </summary>
public record CreatePayRuleRequest
{
    public required int ClientId { get; init; }
    public int? Version { get; init; }

    // Pairing / punch inference
    public decimal? PunchPairResetHours { get; init; }
    public decimal? MaxShiftLengthHours { get; init; }
    public decimal? DistanceBetweenShiftsHours { get; init; }

    // Breaks & lunches
    public int? ExpectedBreakLengthMinutes { get; init; }
    public int? ExpectedLunchLengthMinutes { get; init; }

    // Rounding
    public RoundingStrategy? RoundingStrategy { get; init; }
    public int? RoundingIntervalMinutes { get; init; }
    public int? RoundingGraceMinutes { get; init; }

    // Shift dating & workweek
    public ShiftDateStrategy? ShiftDateStrategy { get; init; }
    public IsoDayOfWeek? WorkweekStartDay { get; init; }

    // Premiums
    public IReadOnlySet<string>? ActivePremiumCodes { get; init; }

    // Overtime
    public decimal? WeeklyOvertimeThresholdHours { get; init; }
    public bool? HasDailyOvertime { get; init; }
    public decimal? DailyOvertimeThresholdHours { get; init; }
    public decimal? DailyDoubletimeThresholdHours { get; init; }
    public bool? HasSeventhDayRule { get; init; }
}
