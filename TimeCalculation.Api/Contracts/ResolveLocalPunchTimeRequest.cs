using NodaTime;

namespace TimeCalculation.Api.Contracts;

/// <summary>Local wall-clock time + the zone it's in, for a caller (the manual/bulk-entry UI) that
/// only has a local date/time picker value and needs the same DST-aware resolution PunchImportRowValidator
/// gives CSV rows. DaylightSaving disambiguates a fall-back-overlap PunchTime the same way the CSV's
/// own DaylightSaving column does — omit it unless the resolve response actually asks for it.</summary>
public record ResolveLocalPunchTimeRequest
{
    public required LocalDateTime PunchTime { get; init; }
    public required string PunchTimeZoneId { get; init; }
    public bool? DaylightSaving { get; init; }
}
