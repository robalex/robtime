using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PunchEndpoints
{
    public static void MapPunchEndpoints(this WebApplication app)
    {
        // POST is open to any authenticated role as of Phase 6.4's self-service clock — but an
        // Employee caller is pinned to their own employee record by EmployeeScopeResolver inside the
        // handler, NOT by this policy. The policy only establishes "signed in with some role"; the
        // per-employee scoping is the handler's job, and skipping it here would let any employee
        // punch as a colleague.
        app.MapPost("/punches", CreatePunch).WithName("CreatePunch")
            .RequireAuthorization(AuthorizationPolicies.Employee);

        // Phase 6.8: same Employee policy + self-scoping as the single-punch route above — a batch is
        // still just "punches for one employee," so the same rule applies (an Employee caller may only
        // batch-enter their own; Supervisor+ may batch-enter for anyone in-tenant).
        app.MapPost("/punches/batch", CreatePunchBatch).WithName("CreatePunchBatch")
            .RequireAuthorization(AuthorizationPolicies.Employee);

        // The remaining four stay Supervisor-or-higher: none are per-employee scoped, so an
        // Employee-role caller could otherwise read/edit/delete any punch just by naming a different
        // EmployeeId/punch id. 6.5 (own timecard) and 6.6 apply the same resolver to the routes they
        // actually need, rather than opening all of these speculatively.
        app.MapGet("/punches", ListPunches).WithName("ListPunches")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapGet("/punches/{id:int}", GetPunch).WithName("GetPunch")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapPut("/punches/{id:int}", UpdatePunch).WithName("UpdatePunch")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapDelete("/punches/{id:int}", DeletePunch).WithName("DeletePunch")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
    }

    private static async Task<Results<Created<PunchResponse>, ValidationProblem, ProblemHttpResult>> CreatePunch(
        CreatePunchRequest request, ClaimsPrincipal user, PunchService service,
        EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var caller = CallerIdentity.FromPrincipal(user);

        // Runs before anything touches the database: an Employee caller may only punch for
        // themselves, and a mismatched EmployeeId is rejected rather than quietly rewritten. For
        // Supervisor+ this passes the requested id straight through, so their behaviour is unchanged.
        var scope = await scopeResolver.ResolveAsync(caller, request.EmployeeId, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return ScopeFailure(scope, "punch creation");
        }

        var result = await service.CreateAsync(request, caller.CognitoSub, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/punches/{result.Value!.Id}", PunchResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch creation."),
        };
    }

    private static async Task<Results<Ok<List<PunchResponse>>, ValidationProblem, ProblemHttpResult>> CreatePunchBatch(
        BatchCreatePunchesRequest request, ClaimsPrincipal user, PunchService service,
        EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        if (request.Punches.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["punches"] = ["At least one punch is required."],
            });
        }

        var caller = CallerIdentity.FromPrincipal(user);

        // Scoped once against the batch's own (validated-consistent, inside the service) EmployeeId —
        // an Employee caller submitting a batch for someone else is rejected before any row is looked
        // at, same as the single-punch route.
        var scope = await scopeResolver.ResolveAsync(caller, request.Punches[0].EmployeeId, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return ScopeFailure(scope, "batch punch creation");
        }

        var result = await service.CreateBatchAsync(request.Punches, caller.CognitoSub, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!.Select(PunchResponse.FromEntity).ToList()),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for batch punch creation."),
        };
    }

    /// <summary>Maps a non-Success <see cref="EmployeeScopeResolver"/> outcome onto a response.
    /// Forbidden becomes a 403 ProblemDetails rather than TypedResults.Forbid() so it carries the
    /// resolver's own explanation ("You can only act on your own employee record") — a bare 403 with
    /// no body is the kind of thing that costs an afternoon to diagnose from the client side.</summary>
    private static ProblemHttpResult ScopeFailure<T>(ServiceResult<T> scope, string operation) => scope.Kind switch
    {
        ServiceResultKind.Forbidden => TypedResults.Problem(
            detail: scope.Detail, statusCode: StatusCodes.Status403Forbidden),
        ServiceResultKind.ValidationFailed => TypedResults.Problem(
            detail: string.Join(" ", scope.ValidationErrors!.SelectMany(e => e.Value)),
            statusCode: StatusCodes.Status400BadRequest),
        _ => throw new InvalidOperationException(
            $"Unexpected {nameof(ServiceResultKind)} '{scope.Kind}' resolving employee scope for {operation}."),
    };

    // from/to are strings, not Instant?/Instant? — NodaTime's Instant has no IParsable<T>/TryParse
    // minimal APIs can discover for query-string binding, so declaring them as Instant? here made
    // the framework infer them as (invalid, for a GET) body parameters instead. Parsed by hand below
    // using the same ISO-8601 pattern NodaTime's own JSON converter uses for Instant in request/
    // response bodies elsewhere in this API, so one format works everywhere a client sends one.
    private static async Task<Results<Ok<PagedResult<PunchResponse>>, ValidationProblem>> ListPunches(
        int employeeId, string? from, string? to, [AsParameters] PagingQuery paging,
        PunchService service, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        Instant? fromInstant = null;
        Instant? toInstant = null;

        if (from is not null)
        {
            var parsed = InstantPattern.ExtendedIso.Parse(from);
            if (parsed.Success)
            {
                fromInstant = parsed.Value;
            }
            else
            {
                errors["from"] = [$"'{from}' is not a valid ISO-8601 instant (e.g. 2026-01-15T08:00:00Z)."];
            }
        }

        if (to is not null)
        {
            var parsed = InstantPattern.ExtendedIso.Parse(to);
            if (parsed.Success)
            {
                toInstant = parsed.Value;
            }
            else
            {
                errors["to"] = [$"'{to}' is not a valid ISO-8601 instant (e.g. 2026-01-15T08:00:00Z)."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var punches = await service.ListAsync(employeeId, fromInstant, toInstant, paging, ct);
        return TypedResults.Ok(new PagedResult<PunchResponse>
        {
            Items = punches.Items.Select(PunchResponse.FromEntity).ToList(),
            TotalCount = punches.TotalCount,
            Page = punches.Page,
            PageSize = punches.PageSize,
        });
    }

    private static async Task<Results<Ok<PunchResponse>, ProblemHttpResult>> GetPunch(
        int id, PunchService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PunchResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch lookup."),
        };
    }

    private static async Task<Results<Ok<PunchResponse>, ValidationProblem, ProblemHttpResult>> UpdatePunch(
        int id, UpdatePunchRequest request, ClaimsPrincipal user, PunchService service, CancellationToken ct)
    {
        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.UpdateAsync(id, request, actorUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PunchResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            // Phase 6.7: the target punch's period is locked (TimecardLockService).
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePunch(
        int id, string? reason, ClaimsPrincipal user, PunchService service, CancellationToken ct)
    {
        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.DeleteAsync(id, actorUserId, reason, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            // Phase 6.7: the target punch's period is locked (TimecardLockService).
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch deletion."),
        };
    }
}
