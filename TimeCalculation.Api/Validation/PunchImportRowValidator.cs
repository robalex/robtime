using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Parses and validates one PunchImportRow into a CreatePunchRequest, or reports exactly why it
/// can't. Two kinds of problem, both surfaced through the same per-field error dictionary every
/// other validator in this API uses: parse failures ("EmployeeId 'abc' is not a valid integer") and
/// business-rule failures. For the latter, this deliberately doesn't reinvent the FixedDollar/
/// FixedHours cross-field rule — once a row parses into a well-typed CreatePunchRequest, it's handed
/// to the existing PunchRequestValidator.Validate, the same check PunchService.CreateAsync/
/// CreateBatchAsync already run, so import can never drift from what a direct punch creation allows.
/// Employee/Position *existence* is checked here instead of by PunchService, because import
/// pre-fetches every employee/valid position id once for the whole file rather than round-tripping
/// the database once per row (see PunchImportService).
///
/// PunchTime is deliberately a *local* date/time, not a pre-resolved Instant — a real export from a
/// prior timekeeping system has local wall-clock times, and requiring someone to hand-convert every
/// row to UTC before uploading just relocates DST bugs into an unvalidated spreadsheet formula
/// instead of catching them here (decided 2026-07-29, after the UTC-only version of this file shipped
/// then got walked back). PunchTimeZoneId says which zone that local time is in — falling back to the
/// row's employee's own HomeTimeZoneId when omitted, since most rows in one file usually share it.
/// Resolving a local time to an Instant has two edge cases DateTimeZone.MapLocal answers precisely:
/// a local time that never happened (the spring-forward gap — always rejected, nothing can fix it)
/// and one that happened twice (the fall-back overlap — resolved via the optional DaylightSaving
/// column, consulted only when the ambiguity is real).
/// </summary>
public static class PunchImportRowValidator
{
    public static ServiceResult<CreatePunchRequest> Validate(
        PunchImportRow row, IReadOnlyDictionary<int, Employee> employeesById, IReadOnlySet<int> validPositionIds)
    {
        var errors = new Dictionary<string, string[]>();

        var employeeId = ParseRequiredInt(row, "EmployeeId", errors);
        Employee? employee = null;
        if (employeeId is { } id)
        {
            if (!employeesById.TryGetValue(id, out employee))
            {
                errors["EmployeeId"] = [$"No employee with id {id} in this client."];
            }
        }

        var localTime = ParseRequiredLocalDateTime(row, "PunchTime", errors);
        var kind = ParseRequiredEnum<PunchKind>(row, "Kind", errors);
        var subtype = ParseOptionalEnum<PunchSubtype>(row, "Subtype", errors);
        var bonusKind = ParseOptionalEnum<BonusKind>(row, "BonusKind", errors);
        var positionId = ParseOptionalInt(row, "PositionId", errors);
        if (positionId is { } posId && !validPositionIds.Contains(posId))
        {
            errors["PositionId"] = [$"No position with id {posId} in this client."];
        }

        var amount = ParseOptionalDecimal(row, "Amount", errors);
        var hours = ParseOptionalDecimal(row, "Hours", errors);
        var countsTowardRegularRate = ParseOptionalBool(row, "CountsTowardRegularRate", errors) ?? false;

        // Only attempted once the local time parsed and we know *some* zone to resolve it against —
        // if EmployeeId was invalid and no explicit PunchTimeZoneId was given, there's no zone to
        // fall back to, and that's already reported as the EmployeeId error above; piling on a second,
        // derived "can't resolve the time" complaint would just be noise on top of the real problem.
        var timeZoneId = ResolveTimeZoneId(row, employee);
        Instant? punchTime = null;
        if (localTime is { } local && timeZoneId is not null)
        {
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId);
            if (zone is null)
            {
                errors["PunchTimeZoneId"] = [$"'{timeZoneId}' is not a recognized time zone."];
            }
            else
            {
                var resolved = LocalTimeResolver.Resolve(local, zone, row.Get("DaylightSaving"));
                if (resolved.Kind == ServiceResultKind.Success)
                {
                    punchTime = resolved.Value;
                }
                else
                {
                    foreach (var (field, messages) in resolved.ValidationErrors!)
                    {
                        errors[field] = messages;
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult<CreatePunchRequest>.ValidationFailed(errors);
        }

        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId!.Value,
            PunchTime = punchTime!.Value,
            PunchTimeZoneId = timeZoneId,
            Kind = kind!.Value,
            Subtype = subtype,
            PositionId = positionId,
            Amount = amount,
            Hours = hours,
            BonusKind = bonusKind,
            CountsTowardRegularRate = countsTowardRegularRate,
        };

        var businessRuleErrors = PunchRequestValidator.Validate(request);
        return businessRuleErrors.Count > 0
            ? ServiceResult<CreatePunchRequest>.ValidationFailed(businessRuleErrors)
            : ServiceResult<CreatePunchRequest>.Success(request);
    }

    private static string? ResolveTimeZoneId(PunchImportRow row, Employee? employee)
    {
        var explicitZone = row.Get("PunchTimeZoneId");
        return string.IsNullOrWhiteSpace(explicitZone) ? employee?.HomeTimeZoneId : explicitZone;
    }

    private static int? ParseRequiredInt(PunchImportRow row, string field, Dictionary<string, string[]> errors)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors[field] = [$"{field} is required."];
            return null;
        }
        if (!int.TryParse(raw, out var value))
        {
            errors[field] = [$"'{raw}' is not a valid whole number."];
            return null;
        }
        return value;
    }

    private static int? ParseOptionalInt(PunchImportRow row, string field, Dictionary<string, string[]> errors)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!int.TryParse(raw, out var value))
        {
            errors[field] = [$"'{raw}' is not a valid whole number."];
            return null;
        }
        return value;
    }

    private static decimal? ParseOptionalDecimal(PunchImportRow row, string field, Dictionary<string, string[]> errors)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!decimal.TryParse(raw, out var value))
        {
            errors[field] = [$"'{raw}' is not a valid number."];
            return null;
        }
        return value;
    }

    private static bool? ParseOptionalBool(PunchImportRow row, string field, Dictionary<string, string[]> errors)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!bool.TryParse(raw, out var value))
        {
            errors[field] = [$"'{raw}' is not 'true' or 'false'."];
            return null;
        }
        return value;
    }

    // Local date/time only — no UTC offset or "Z" suffix. A value that includes one (someone pasting
    // an already-resolved Instant by mistake) fails to parse against this pattern and reports as a
    // normal per-field error rather than being silently misread.
    private static LocalDateTime? ParseRequiredLocalDateTime(PunchImportRow row, string field, Dictionary<string, string[]> errors)
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors[field] = [$"{field} is required."];
            return null;
        }
        var parsed = LocalDateTimePattern.ExtendedIso.Parse(raw);
        if (!parsed.Success)
        {
            errors[field] = [
                $"'{raw}' is not a valid local date/time (e.g. 2026-01-15T08:00:00) — no UTC offset or 'Z', " +
                "just the wall-clock time where the punch happened.",
            ];
            return null;
        }
        return parsed.Value;
    }

    private static TEnum? ParseRequiredEnum<TEnum>(PunchImportRow row, string field, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors[field] = [$"{field} is required."];
            return null;
        }
        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value))
        {
            errors[field] = [$"'{raw}' is not a valid {field} — expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}."];
            return null;
        }
        return value;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(PunchImportRow row, string field, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        var raw = row.Get(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value))
        {
            errors[field] = [$"'{raw}' is not a valid {field} — expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}."];
            return null;
        }
        return value;
    }
}
