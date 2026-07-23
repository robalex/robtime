using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Fields shared between <see cref="CreatePayRuleRequest"/> and <see cref="UpdatePayRuleRequest"/> —
/// every field here is optional on both, though what "omitted" means differs per request (Create
/// falls back to PayRule's own default; Update leaves the existing persisted value alone). Only
/// ClientId/Name (Create requires both; Update makes Name optional too) live on the concrete types,
/// since those are the only fields whose required-ness actually differs between the two.
/// </summary>
public abstract record PayRuleFieldsRequest
{
    public string? Description { get; init; }
    public string? TemplateCode { get; init; }
    public int? TemplateVersion { get; init; }

    public decimal? PunchPairResetHours { get; init; }
    public decimal? MaxShiftLengthHours { get; init; }
    public decimal? DistanceBetweenShiftsHours { get; init; }

    public int? ExpectedBreakLengthMinutes { get; init; }
    public int? ExpectedLunchLengthMinutes { get; init; }

    public RoundingStrategy? RoundingStrategy { get; init; }
    public int? RoundingIntervalMinutes { get; init; }
    public int? RoundingGraceMinutes { get; init; }

    public ShiftDateStrategy? ShiftDateStrategy { get; init; }
    public IsoDayOfWeek? WorkweekStartDay { get; init; }

    public IReadOnlySet<string>? ActivePremiumCodes { get; init; }
    public IReadOnlySet<string>? ActiveDifferentialCodes { get; init; }

    public decimal? WeeklyOvertimeThresholdHours { get; init; }
    public bool? HasDailyOvertime { get; init; }
    public decimal? DailyOvertimeThresholdHours { get; init; }
    public decimal? DailyDoubletimeThresholdHours { get; init; }
    public bool? HasSeventhDayRule { get; init; }
}
