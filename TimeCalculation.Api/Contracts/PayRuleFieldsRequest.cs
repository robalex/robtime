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

    // HashSet, not IReadOnlySet — System.Text.Json can't deserialize an interface-typed collection
    // without a custom converter (found via TimeCalculation.Api.Tests actually round-tripping this
    // DTO through JSON, not just checking it serialized). Wire-format DTOs need to be shaped for
    // what JSON (de)serialization actually supports; that's a different concern from the domain
    // model's own IReadOnlySet<string>, which never goes through this deserializer.
    public HashSet<string>? ActivePremiumCodes { get; init; }
    public HashSet<string>? ActiveDifferentialCodes { get; init; }

    public decimal? WeeklyOvertimeThresholdHours { get; init; }
    public bool? HasDailyOvertime { get; init; }
    public decimal? DailyOvertimeThresholdHours { get; init; }
    public decimal? DailyDoubletimeThresholdHours { get; init; }
    public bool? HasSeventhDayRule { get; init; }
}
