using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PayrollEarningCodeMappingEndpoints
{
    public static void MapPayrollEarningCodeMappingEndpoints(this WebApplication app)
    {
        // Nested under the profile: a mapping has no meaning apart from the export config it's for.
        app.MapGet("/payroll-export-profiles/{profileId:int}/earning-codes", ListMappings)
            .WithName("ListPayrollEarningCodeMappings").RequireAuthorization();
        app.MapPost("/payroll-export-profiles/{profileId:int}/earning-codes", CreateMapping)
            .WithName("CreatePayrollEarningCodeMapping").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapPut("/payroll-export-profiles/{profileId:int}/earning-codes/{id:int}", UpdateMapping)
            .WithName("UpdatePayrollEarningCodeMapping").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/payroll-export-profiles/{profileId:int}/earning-codes/{id:int}", DeleteMapping)
            .WithName("DeletePayrollEarningCodeMapping").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Ok<List<PayrollEarningCodeMappingResponse>>, ProblemHttpResult>> ListMappings(
        int profileId, PayrollEarningCodeMappingService service, CancellationToken ct)
    {
        var result = await service.ListAsync(profileId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(
                result.Value!.Select(PayrollEarningCodeMappingResponse.FromEntity).ToList()),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' listing earning code mappings."),
        };
    }

    private static async Task<Results<Created<PayrollEarningCodeMappingResponse>, ValidationProblem, ProblemHttpResult>> CreateMapping(
        int profileId, CreatePayrollEarningCodeMappingRequest request, PayrollEarningCodeMappingService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(profileId, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/payroll-export-profiles/{profileId}/earning-codes/{result.Value!.Id}",
                PayrollEarningCodeMappingResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' creating an earning code mapping."),
        };
    }

    private static async Task<Results<Ok<PayrollEarningCodeMappingResponse>, ValidationProblem, ProblemHttpResult>> UpdateMapping(
        int profileId, int id, UpdatePayrollEarningCodeMappingRequest request, PayrollEarningCodeMappingService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(profileId, id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayrollEarningCodeMappingResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' updating an earning code mapping."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteMapping(
        int profileId, int id, PayrollEarningCodeMappingService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(profileId, id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' deleting an earning code mapping."),
        };
    }
}
