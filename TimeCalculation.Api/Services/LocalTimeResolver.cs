using NodaTime;
using NodaTime.Text;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Resolves a local wall-clock time + IANA zone into an unambiguous Instant — the one piece of real
/// DST logic every local-time-entry path in this API needs, extracted so punch import (CSV rows) and
/// the manual/bulk-entry UI's resolve endpoint give byte-for-byte identical answers (and error
/// wording) for the same input instead of two DST implementations silently drifting apart.
///
/// DateTimeZone.MapLocal tells us, for this exact local time in this exact zone, how many real
/// instants it could mean: 0 (the spring-forward gap swallowed it — reject, there's no right answer),
/// 1 (the normal case), or 2 (the fall-back overlap — First() is the earlier instant, still in
/// daylight time; Last() is the later one, already back on standard time). daylightSavingRaw is a raw
/// "true"/"false" string rather than a bool? so callers with an actual raw string (CSV) and callers
/// with a typed bool (JSON) can both funnel through the exact same parse-and-validate path.
/// </summary>
public static class LocalTimeResolver
{
    public static ServiceResult<Instant> Resolve(LocalDateTime local, DateTimeZone zone, string? daylightSavingRaw)
    {
        var mapping = zone.MapLocal(local);
        switch (mapping.Count)
        {
            case 0:
                return ServiceResult<Instant>.ValidationFailed(new Dictionary<string, string[]>
                {
                    ["PunchTime"] = [
                        $"{FormatLocal(local)} does not exist in {zone.Id} — it falls in the gap where clocks " +
                        "skip forward for daylight saving time.",
                    ],
                });

            case 1:
                return ServiceResult<Instant>.Success(mapping.Single().ToInstant());

            default: // 2 — the fall-back overlap
                if (string.IsNullOrWhiteSpace(daylightSavingRaw))
                {
                    return ServiceResult<Instant>.ValidationFailed(new Dictionary<string, string[]>
                    {
                        ["DaylightSaving"] = [
                            $"{FormatLocal(local)} is ambiguous in {zone.Id} — it happens twice because clocks " +
                            "fall back for daylight saving time. Set DaylightSaving to true (before the change) " +
                            "or false (after) to say which one this is.",
                        ],
                    });
                }
                if (!bool.TryParse(daylightSavingRaw, out var isDaylightSaving))
                {
                    return ServiceResult<Instant>.ValidationFailed(new Dictionary<string, string[]>
                    {
                        ["DaylightSaving"] = [$"'{daylightSavingRaw}' is not 'true' or 'false'."],
                    });
                }
                return ServiceResult<Instant>.Success((isDaylightSaving ? mapping.First() : mapping.Last()).ToInstant());
        }
    }

    public static string FormatLocal(LocalDateTime local) => LocalDateTimePattern.ExtendedIso.Format(local);
}
