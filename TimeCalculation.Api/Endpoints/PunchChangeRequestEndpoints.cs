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
        // Supervisor-or-higher for all four, same reasoning as PunchEndpoints: none of these are
        // per-employee scoped yet. An Employee submitting requests for their own punches — the actual
        // point of this table — opens up in Phase 6.4 alongside the self-service scoping check;
        // deciding a request stays Supervisor+ permanently either way ("a supervisor (or above)
        // approves or denies it," UI_PLAN.md's Phase 6 design).
        app.MapPost("/punch-change-requests", SubmitPunchChangeRequest).WithName("SubmitPunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapGet("/punch-change-requests", ListPunchChangeRequests).WithName("ListPunchChangeRequests")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapGet("/punch-change-requests/{id:int}", GetPunchChangeRequest).WithName("GetPunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
        app.MapPost("/punch-change-requests/{id:int}/decide", DecidePunchChangeRequest).WithName("DecidePunchChangeRequest")
            .RequireAuthorization(AuthorizationPolicies.Supervisor);
    }

    private static async Task<Results<Created<PunchChangeRequestResponse>, ValidationProblem, ProblemHttpResult>> SubmitPunchChangeRequest(
        SubmitPunchChangeRequestRequest request, ClaimsPrincipal user, PunchChangeRequestService service, CancellationToken ct)
    {
        var requesterUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.SubmitAsync(request, requesterUserId, ct);
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
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch change request submission."),
        };
    }

    private static async Task<Ok<PagedResult<PunchChangeRequestResponse>>> ListPunchChangeRequests(
        PunchChangeRequestStatus? status, int? employeeId, [AsParameters] PagingQuery paging,
        PunchChangeRequestService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(status, employeeId, paging, ct));

    private static async Task<Results<Ok<PunchChangeRequestResponse>, ProblemHttpResult>> GetPunchChangeRequest(
        int id, PunchChangeRequestService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch change request lookup."),
        };
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
