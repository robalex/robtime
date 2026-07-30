using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Read-only diagnostic for setting up differentials: projects where every DifferentialRule a given
/// PayRule enables (PayRule.ActiveDifferentialCodes) would apply over a date window, using the real
/// employee's timezone and an optional real HolidayCalendar — the same PipelineContext-building
/// pattern PayRuleWhatIfService uses. When the caller supplies TestPunches, they're resolved through
/// LocalTimeResolver (the same DST-aware local-time resolution punch import and manual entry use)
/// into real Punch objects and run through DifferentialExplainer, so the response's Shifts show
/// exactly which differentials applied and why. Never touches Punches — test punches are built
/// in-memory and never saved.
/// </summary>
public class DifferentialSandboxService(PayrollDbContext db, IClock clock)
{
    private static readonly int[] AllowedDayCounts = [7, 14];

    public async Task<ServiceResult<DifferentialSandboxResponse>> RunAsync(
        DifferentialSandboxRequest request, CancellationToken ct)
    {
        if (!AllowedDayCounts.Contains(request.DayCount))
        {
            return ServiceResult<DifferentialSandboxResponse>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["dayCount"] = ["dayCount must be 7 or 14."],
            });
        }

        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == request.PayRuleId, ct);
        if (payRule is null)
        {
            return ServiceResult<DifferentialSandboxResponse>.NotFound($"No pay rule with id {request.PayRuleId}.");
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null)
        {
            return ServiceResult<DifferentialSandboxResponse>.NotFound($"No employee with id {request.EmployeeId}.");
        }

        HolidayCalendar? holidayCalendar = null;
        if (request.HolidayCalendarId is { } holidayCalendarId)
        {
            holidayCalendar = await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == holidayCalendarId, ct);
            if (holidayCalendar is null)
            {
                return ServiceResult<DifferentialSandboxResponse>.NotFound(
                    $"No holiday calendar with id {holidayCalendarId}.");
            }
        }

        var resolvedPunches = ResolveTestPunches(request.TestPunches, employee, clock.GetCurrentInstant());
        if (resolvedPunches.Kind != ServiceResultKind.Success)
        {
            return ServiceResult<DifferentialSandboxResponse>.ValidationFailed(resolvedPunches.ValidationErrors!);
        }

        // Only the rules this PayRule actually opts into — a zone for a rule the pay rule never
        // enables would never fire in real payroll, and showing it would be noise, not signal (same
        // gating DifferentialApplier applies via ActiveDifferentialCodes).
        var allDifferentialRules = await db.DifferentialRules.ToListAsync(ct);
        var activeRules = allDifferentialRules
            .Where(r => payRule.ActiveDifferentialCodes.Contains(r.Code))
            .ToList();

        var windowEnd = request.WindowStart.PlusDays(request.DayCount - 1);

        // Same padding trick as PayRuleWhatIfService, and for the same reason: forcing the rule to
        // cover more than [WindowStart, WindowEnd] guarantees GetRuleAt never gaps for anything the
        // zone projector or explainer might look at, whatever the window turns out to need.
        //
        // ctx carries *every* client differential (not just activeRules) — DifferentialExplainer
        // needs the full set to correctly report NotEnabledByPayRule for a rule the pay rule doesn't
        // opt into, which is likely the single most common reason someone opens this tool. Zone
        // projection below still only runs over activeRules, so the calendar itself stays limited to
        // what could actually fire.
        var ctx = new PipelineContext(
            employee,
            [new PayRuleAssignment(payRule, request.WindowStart.PlusDays(-7), windowEnd.PlusDays(7))],
            [],
            allDifferentialRules,
            holidayCalendar);

        var zones = activeRules
            .SelectMany(rule => DifferentialZoneProjector.Project(rule, request.WindowStart, windowEnd, ctx))
            .Select(DifferentialZoneResponse.FromDomain)
            .ToList();

        var shifts = DifferentialExplainer.Explain(resolvedPunches.Value!, ctx)
            .Select(ShiftDifferentialExplanationResponse.FromDomain)
            .ToList();

        return ServiceResult<DifferentialSandboxResponse>.Success(new DifferentialSandboxResponse
        {
            WindowStart = request.WindowStart,
            DayCount = request.DayCount,
            Zones = zones,
            Shifts = shifts,
        });
    }

    // Local wall-clock time + zone, resolved exactly like punch import's rows and manual entry's
    // resolve-local-time endpoint — a spring-forward gap or unresolved fall-back ambiguity reports
    // the same row-scoped error shape ("testPunches[i].Field") rather than being silently misread.
    // Synthetic negative ids, matching TimecardService.PreviewAsync's draft-punch convention — these
    // are never persisted, so there's no real id to give them.
    private static ServiceResult<List<Punch>> ResolveTestPunches(
        IReadOnlyList<SandboxTestPunch> testPunches, Employee employee, Instant now)
    {
        var punches = new List<Punch>();
        var errors = new Dictionary<string, string[]>();

        for (var i = 0; i < testPunches.Count; i++)
        {
            var testPunch = testPunches[i];
            var zoneId = string.IsNullOrWhiteSpace(testPunch.PunchTimeZoneId)
                ? employee.HomeTimeZoneId
                : testPunch.PunchTimeZoneId;
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId);
            if (zone is null)
            {
                errors[$"testPunches[{i}].PunchTimeZoneId"] = [$"'{zoneId}' is not a recognized time zone."];
                continue;
            }

            var resolved = LocalTimeResolver.Resolve(testPunch.PunchTime, zone, testPunch.DaylightSaving?.ToString());
            if (resolved.Kind != ServiceResultKind.Success)
            {
                foreach (var (field, messages) in resolved.ValidationErrors!)
                {
                    errors[$"testPunches[{i}].{field}"] = messages;
                }
                continue;
            }

            punches.Add(new Punch
            {
                Id = -(i + 1),
                ClientId = employee.ClientId,
                EmployeeId = employee.Id,
                Employee = employee,
                PunchTime = resolved.Value,
                PunchTimeZoneId = zoneId,
                Kind = testPunch.Kind,
                CreatedAt = now,
                CreatedBy = "sandbox",
            });
        }

        return errors.Count > 0
            ? ServiceResult<List<Punch>>.ValidationFailed(errors)
            : ServiceResult<List<Punch>>.Success(punches);
    }
}
