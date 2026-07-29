using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class TimecardEndpoints
{
    public static void MapTimecardEndpoints(this WebApplication app)
    {
        // Opened to Employee (Phase 6.5) — EmployeeScopeResolver pins an Employee caller to their own
        // record inside the handler, same pattern PunchEndpoints' CreatePunch already established:
        // the policy only proves "signed in with some role," never "may read this employeeId."
        // Supervisor+ is unaffected — the resolver passes their requested id straight through.
        app.MapGet("/employees/{id:int}/timecard", GetTimecard).WithName("GetTimecard")
            .RequireAuthorization(AuthorizationPolicies.Employee);

        // Approve/un-approve stay Supervisor+ only, permanently — UI_PLAN.md decision 21 never opens
        // this to Employee the way the plain read above was. No EmployeeScopeResolver needed: a
        // Supervisor+ caller already acts on any employee in-tenant.
        app.MapPost("/employees/{id:int}/timecard/approve", ApproveTimecard).WithName("ApproveTimecard")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapPost("/employees/{id:int}/timecard/unapprove", UnapproveTimecard).WithName("UnapproveTimecard")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);

        // Phase 6.8's bulk-entry grid: same Employee-policy + EmployeeScopeResolver pattern as
        // GetTimecard above — a preview is just another read, so it gets the same self-service access.
        app.MapPost("/employees/{id:int}/timecard/preview", PreviewTimecard).WithName("PreviewTimecard")
            .RequireAuthorization(AuthorizationPolicies.Employee);
    }

    // date is a string, not LocalDate? — same reason PunchEndpoints' from/to are strings: NodaTime
    // types have no IParsable<T>/TryParse minimal APIs can discover for query binding. Parsed here
    // with the same ISO-8601 convention the rest of this API's date fields already use.
    private static async Task<Results<Ok<TimecardResponse>, ValidationProblem, ProblemHttpResult>> GetTimecard(
        int id, string? date, ClaimsPrincipal user, TimecardService service,
        EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var parsed = ParseDate(date);
        if (parsed.Kind is not ServiceResultKind.Success)
        {
            return ValidationFailure(parsed);
        }

        var caller = CallerIdentity.FromPrincipal(user);
        var scope = await scopeResolver.ResolveAsync(caller, id, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return ScopeFailure(scope);
        }

        // Not scope.Value: on Success it's always exactly `id` (the resolver either passed a
        // Supervisor+'s requested id straight through or confirmed it matches the Employee caller's
        // own), same convention PunchEndpoints' CreatePunch already uses.
        var result = await service.GetAsync(
            id, parsed.Value, callerIsEmployeeRole: caller.Role is AppRole.Employee, ct);
        return ToResult(result, "timecard lookup");
    }

    private static async Task<Results<Ok<TimecardResponse>, ValidationProblem, ProblemHttpResult>> ApproveTimecard(
        int id, string? date, ClaimsPrincipal user, TimecardService service, CancellationToken ct)
    {
        var parsed = ParseDate(date);
        if (parsed.Kind is not ServiceResultKind.Success)
        {
            return ValidationFailure(parsed);
        }

        var approverUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.ApproveAsync(id, parsed.Value, approverUserId, ct);
        return ToResult(result, "timecard approval");
    }

    private static async Task<Results<Ok<TimecardResponse>, ValidationProblem, ProblemHttpResult>> UnapproveTimecard(
        int id, string? date, ClaimsPrincipal user, TimecardService service, CancellationToken ct)
    {
        var parsed = ParseDate(date);
        if (parsed.Kind is not ServiceResultKind.Success)
        {
            return ValidationFailure(parsed);
        }

        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.UnapproveAsync(id, parsed.Value, actorUserId, ct);
        return ToResult(result, "timecard un-approval");
    }

    private static async Task<Results<Ok<BulkPunchPreviewResponse>, ValidationProblem, ProblemHttpResult>> PreviewTimecard(
        int id, string? date, PreviewPunchesRequest request, ClaimsPrincipal user, TimecardService service,
        EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var parsed = ParseDate(date);
        if (parsed.Kind is not ServiceResultKind.Success)
        {
            return ValidationFailure(parsed);
        }

        var caller = CallerIdentity.FromPrincipal(user);
        var scope = await scopeResolver.ResolveAsync(caller, id, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return ScopeFailure(scope);
        }

        var result = await service.PreviewAsync(id, parsed.Value, request.DraftPunches, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for timecard preview."),
        };
    }

    private static ServiceResult<LocalDate?> ParseDate(string? date)
    {
        if (date is null)
        {
            return ServiceResult<LocalDate?>.Success(null);
        }

        var parsed = LocalDatePattern.Iso.Parse(date);
        if (!parsed.Success)
        {
            return ServiceResult<LocalDate?>.ValidationFailed(new Dictionary<string, string[]>
            {
                ["date"] = [$"'{date}' is not a valid ISO-8601 date (e.g. 2026-01-15)."],
            });
        }

        return ServiceResult<LocalDate?>.Success(parsed.Value);
    }

    private static ValidationProblem ValidationFailure(ServiceResult<LocalDate?> parsed) =>
        TypedResults.ValidationProblem(parsed.ValidationErrors!);

    private static Results<Ok<TimecardResponse>, ValidationProblem, ProblemHttpResult> ToResult(
        ServiceResult<TimecardResponse> result, string operationName) => result.Kind switch
    {
        ServiceResultKind.Success => TypedResults.Ok(result.Value!),
        ServiceResultKind.NotFound => TypedResults.Problem(
            detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
        ServiceResultKind.Conflict => TypedResults.Problem(
            detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
        _ => throw new InvalidOperationException(
            $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for {operationName}."),
    };

    // Duplicated from PunchEndpoints rather than shared: both are small, and the two call sites
    // return different Results<> unions (this one has no NotFound branch to fold Forbidden into),
    // so a shared helper would need its own generic return-shape gymnastics for a three-line switch.
    private static ProblemHttpResult ScopeFailure<T>(ServiceResult<T> scope) => scope.Kind switch
    {
        ServiceResultKind.Forbidden => TypedResults.Problem(
            detail: scope.Detail, statusCode: StatusCodes.Status403Forbidden),
        ServiceResultKind.ValidationFailed => TypedResults.Problem(
            detail: string.Join(" ", scope.ValidationErrors!.SelectMany(e => e.Value)),
            statusCode: StatusCodes.Status400BadRequest),
        _ => throw new InvalidOperationException(
            $"Unexpected {nameof(ServiceResultKind)} '{scope.Kind}' resolving employee scope for timecard lookup."),
    };
}
