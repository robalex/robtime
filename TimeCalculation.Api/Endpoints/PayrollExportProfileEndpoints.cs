using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PayrollExportProfileEndpoints
{
    public static void MapPayrollExportProfileEndpoints(this WebApplication app)
    {
        app.MapPost("/payroll-export-profiles", CreateProfile).WithName("CreatePayrollExportProfile")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapGet("/payroll-export-profiles", ListProfiles).WithName("ListPayrollExportProfiles").RequireAuthorization();
        app.MapGet("/payroll-export-profiles/{id:int}", GetProfile).WithName("GetPayrollExportProfile").RequireAuthorization();
        app.MapPut("/payroll-export-profiles/{id:int}", UpdateProfile).WithName("UpdatePayrollExportProfile")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/payroll-export-profiles/{id:int}", DeleteProfile).WithName("DeletePayrollExportProfile")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<PayrollExportProfileResponse>, ValidationProblem, ProblemHttpResult>> CreateProfile(
        CreatePayrollExportProfileRequest request, PayrollExportProfileService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/payroll-export-profiles/{result.Value!.Id}", PayrollExportProfileResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for payroll export profile creation."),
        };
    }

    private static async Task<Ok<PagedResult<PayrollExportProfileResponse>>> ListProfiles(
        int clientId, [AsParameters] PagingQuery paging, PayrollExportProfileService service, CancellationToken ct)
    {
        var profiles = await service.ListAsync(clientId, paging, ct);
        return TypedResults.Ok(new PagedResult<PayrollExportProfileResponse>
        {
            Items = profiles.Items.Select(PayrollExportProfileResponse.FromEntity).ToList(),
            TotalCount = profiles.TotalCount,
            Page = profiles.Page,
            PageSize = profiles.PageSize,
        });
    }

    private static async Task<Results<Ok<PayrollExportProfileResponse>, ProblemHttpResult>> GetProfile(
        int id, PayrollExportProfileService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayrollExportProfileResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for payroll export profile lookup."),
        };
    }

    private static async Task<Results<Ok<PayrollExportProfileResponse>, ValidationProblem, ProblemHttpResult>> UpdateProfile(
        int id, UpdatePayrollExportProfileRequest request, PayrollExportProfileService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayrollExportProfileResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for payroll export profile update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteProfile(
        int id, PayrollExportProfileService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for payroll export profile deletion."),
        };
    }
}
