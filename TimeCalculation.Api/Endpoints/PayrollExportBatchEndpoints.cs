using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PayrollExportBatchEndpoints
{
    public static void MapPayrollExportBatchEndpoints(this WebApplication app)
    {
        // ClientAdmin throughout, not Supervisor+ — same reasoning PunchImportEndpoints' own comment
        // gives for its bulk-action routes: one export run affects every employee in the period at
        // once, closer in kind to Position/PayRule administration than a single supervisor's
        // row-scoped edit. No existing route in this repo establishes a narrower "can trigger, can't
        // configure" split, and this isn't the place to invent one.
        app.MapPost("/payroll-export-profiles/{profileId:int}/exports", CreateExport)
            .WithName("CreatePayrollExport").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapGet("/payroll-export-profiles/{profileId:int}/exports", ListExports)
            .WithName("ListPayrollExports").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapGet("/payroll-export-profiles/{profileId:int}/exports/{id:int}/download", DownloadExport)
            .WithName("DownloadPayrollExport").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapPost("/payroll-export-profiles/{profileId:int}/exports/{id:int}/void", VoidExport)
            .WithName("VoidPayrollExport").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<PayrollExportBatchResponse>, ValidationProblem, ProblemHttpResult>> CreateExport(
        int profileId, CreatePayrollExportRequest request, ClaimsPrincipal user, PayrollExportService service, CancellationToken ct)
    {
        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.CreateExportAsync(profileId, request, actorUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/payroll-export-profiles/{profileId}/exports/{result.Value!.Id}",
                PayrollExportBatchResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            // No client selected — only reachable for a SystemAdmin who hasn't chosen one.
            ServiceResultKind.Forbidden => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for payroll export creation."),
        };
    }

    private static async Task<Results<Ok<PagedResult<PayrollExportBatchResponse>>, ProblemHttpResult>> ListExports(
        int profileId, [AsParameters] PagingQuery paging, PayrollExportService service, CancellationToken ct)
    {
        var result = await service.ListBatchesAsync(profileId, paging, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(new PagedResult<PayrollExportBatchResponse>
            {
                Items = result.Value!.Items.Select(PayrollExportBatchResponse.FromEntity).ToList(),
                TotalCount = result.Value.TotalCount,
                Page = result.Value.Page,
                PageSize = result.Value.PageSize,
            }),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' listing payroll exports."),
        };
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadExport(
        int profileId, int id, PayrollExportService service, CancellationToken ct)
    {
        var result = await service.GetBatchAsync(profileId, id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.File(
                result.Value!.FileContent, "text/csv", result.Value.FileName),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' downloading a payroll export."),
        };
    }

    private static async Task<Results<Ok<PayrollExportBatchResponse>, ProblemHttpResult>> VoidExport(
        int profileId, int id, ClaimsPrincipal user, PayrollExportService service, CancellationToken ct)
    {
        var actorUserId = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.VoidBatchAsync(profileId, id, actorUserId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayrollExportBatchResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' voiding a payroll export."),
        };
    }
}
