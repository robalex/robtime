using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PunchImportEndpoints
{
    public static void MapPunchImportEndpoints(this WebApplication app)
    {
        // ClientAdmin+, not the Employee-scoped self-service pattern PunchEndpoints' batch route
        // uses — one import call can create or hard-delete punches for any employee in the tenant at
        // once, closer in kind to Position/PayRule administration than to a supervisor fixing one
        // employee's shift.
        // DisableAntiforgery: minimal APIs auto-attach antiforgery metadata to any endpoint that
        // binds form data (including IFormFile), on the assumption it might be a cookie-authenticated
        // browser form post. This API has no cookie/session auth surface at all (bearer JWT only, per
        // UI_PLAN.md §5's "never authorize on cookie presence") and never registers antiforgery
        // middleware, so that default would otherwise make every request to this endpoint 500.
        app.MapPost("/punch-imports", ImportPunches).WithName("ImportPunches")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin)
            .DisableAntiforgery();
        app.MapGet("/punch-imports", ListPunchImportBatches).WithName("ListPunchImportBatches")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/punch-imports/{id:int}", DeletePunchImportBatch).WithName("DeletePunchImportBatch")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<PunchImportBatchResponse>, ValidationProblem, ProblemHttpResult>> ImportPunches(
        IFormFile file, ClaimsPrincipal user, PunchImportService service, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["The file is empty."],
            });
        }

        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        await using var stream = file.OpenReadStream();
        var result = await service.ImportAsync(stream, file.FileName, actorUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/punch-imports/{result.Value!.Id}", result.Value),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            // The whole file would land in a locked period (TimecardLockService.CheckBulkAsync).
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            // No client selected — only reachable for a SystemAdmin who hasn't chosen one.
            ServiceResultKind.Forbidden => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch import."),
        };
    }

    private static async Task<Ok<PagedResult<PunchImportBatchResponse>>> ListPunchImportBatches(
        [AsParameters] PagingQuery paging, PunchImportService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListBatchesAsync(paging, ct));

    private static async Task<Results<Ok<PunchImportBatchResponse>, ProblemHttpResult>> DeletePunchImportBatch(
        int id, ClaimsPrincipal user, PunchImportService service, CancellationToken ct)
    {
        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.DeleteBatchAsync(id, actorUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch import batch deletion."),
        };
    }
}
