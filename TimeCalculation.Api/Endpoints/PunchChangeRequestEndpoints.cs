using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class PunchChangeRequestEndpoints
{
    public static void MapPunchChangeRequestEndpoints(this WebApplication app)
    {
        // Submit/list/get are open to Employee — self-scoped inside each handler via
        // ResolveCallerScopeAsync below, same split EmployeeScopeResolver enforces elsewhere: an
        // Employee caller only ever acts on/sees their own requests, Supervisor+ is unrestricted.
        // This is what "an Employee submitting requests for their own punches — the actual point of
        // this table" (the comment that used to sit here) actually means, done now rather than left
        // for a later phase that never came back to it. Deciding a request stays Supervisor+
        // permanently ("a supervisor (or above) approves or denies it," UI_PLAN.md's Phase 6 design).
        app.MapPost("/punch-change-requests", SubmitPunchChangeRequest).WithName("SubmitPunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Employee);
        app.MapGet("/punch-change-requests", ListPunchChangeRequests).WithName("ListPunchChangeRequests")
            .RequireAuthorization(AuthorizationPolicies.Employee);
        app.MapGet("/punch-change-requests/{id:int}", GetPunchChangeRequest).WithName("GetPunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Employee);
        app.MapPost("/punch-change-requests/{id:int}/decide", DecidePunchChangeRequest).WithName("DecidePunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
    }

    private static async Task<Results<Created<PunchChangeRequestResponse>, ValidationProblem, ProblemHttpResult>> SubmitPunchChangeRequest(
        SubmitPunchChangeRequestRequest request, ClaimsPrincipal user, PunchChangeRequestService service,
        EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var caller = CallerIdentity.FromPrincipal(user);
        var scope = await ResolveCallerScopeAsync(caller, scopeResolver, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return TypedResults.Problem(detail: scope.Detail, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.SubmitAsync(request, caller.CognitoSub, scope.Value, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/punch-change-requests/{result.Value!.Id}", PunchChangeRequestResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            // Phase 6.7: the target/requested period is locked (TimecardLockService).
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            // An Employee caller targeting someone else's punch/employee id (SubmitAsync's own check
            // — the target employee for Edit/Delete isn't known until it looks up the punch, so this
            // can't be caught by ResolveCallerScopeAsync above).
            ServiceResultKind.Forbidden => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch change request submission."),
        };
    }

    private static async Task<Results<Ok<PagedResult<PunchChangeRequestResponse>>, ProblemHttpResult>> ListPunchChangeRequests(
        PunchChangeRequestStatus? status, int? employeeId, ClaimsPrincipal user, [AsParameters] PagingQuery paging,
        PunchChangeRequestService service, EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var caller = CallerIdentity.FromPrincipal(user);
        var scope = await ResolveCallerScopeAsync(caller, scopeResolver, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return TypedResults.Problem(detail: scope.Detail, statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(await service.ListAsync(status, employeeId, scope.Value, paging, ct));
    }

    private static async Task<Results<Ok<PunchChangeRequestResponse>, ProblemHttpResult>> GetPunchChangeRequest(
        int id, ClaimsPrincipal user, PunchChangeRequestService service, EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        var caller = CallerIdentity.FromPrincipal(user);
        var scope = await ResolveCallerScopeAsync(caller, scopeResolver, ct);
        if (scope.Kind is not ServiceResultKind.Success)
        {
            return TypedResults.Problem(detail: scope.Detail, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.GetAsync(id, scope.Value, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Forbidden => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch change request lookup."),
        };
    }

    /// <summary>Resolves the employee id an Employee-role caller is pinned to (Forbidden if their
    /// account has no linked employee record), or Success(null) for Supervisor+ meaning "no
    /// restriction" — shared by the three routes above that open to Employee. Not
    /// EmployeeScopeResolver.ResolveAsync itself: that method requires an explicit target id for a
    /// Supervisor+ caller, which these self-viewing/self-submitting routes don't have one of.</summary>
    private static async Task<ServiceResult<int?>> ResolveCallerScopeAsync(
        CallerIdentity caller, EmployeeScopeResolver scopeResolver, CancellationToken ct)
    {
        if (caller.Role is not AppRole.Employee)
        {
            return ServiceResult<int?>.Success(null);
        }

        var ownEmployeeId = await scopeResolver.ResolveOwnAsync(caller, ct);
        return ownEmployeeId is { } id
            ? ServiceResult<int?>.Success(id)
            : ServiceResult<int?>.Forbidden(
                "This account has no employee record linked to it, so it cannot access punch change requests.");
    }

    private static async Task<Results<Ok<PunchChangeRequestResponse>, ValidationProblem, ProblemHttpResult>> DecidePunchChangeRequest(
        int id, DecidePunchChangeRequestRequest request, ClaimsPrincipal user,
        PunchChangeRequestService service, CancellationToken ct)
    {
        var reviewerUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.DecideAsync(id, request, reviewerUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PunchChangeRequestResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch change request decision."),
        };
    }
}
