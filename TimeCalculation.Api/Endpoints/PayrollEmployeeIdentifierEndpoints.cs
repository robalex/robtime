using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PayrollEmployeeIdentifierEndpoints
{
    public static void MapPayrollEmployeeIdentifierEndpoints(this WebApplication app)
    {
        app.MapGet("/payroll-export-profiles/{profileId:int}/employee-identifiers", ListIdentifiers)
            .WithName("ListPayrollEmployeeIdentifiers").RequireAuthorization();
        app.MapPost("/payroll-export-profiles/{profileId:int}/employee-identifiers", CreateIdentifier)
            .WithName("CreatePayrollEmployeeIdentifier").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapPut("/payroll-export-profiles/{profileId:int}/employee-identifiers/{id:int}", UpdateIdentifier)
            .WithName("UpdatePayrollEmployeeIdentifier").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/payroll-export-profiles/{profileId:int}/employee-identifiers/{id:int}", DeleteIdentifier)
            .WithName("DeletePayrollEmployeeIdentifier").RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Ok<List<PayrollEmployeeIdentifierResponse>>, ProblemHttpResult>> ListIdentifiers(
        int profileId, PayrollEmployeeIdentifierService service, CancellationToken ct)
    {
        var result = await service.ListAsync(profileId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(
                result.Value!.Select(PayrollEmployeeIdentifierResponse.FromEntity).ToList()),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' listing employee identifiers."),
        };
    }

    private static async Task<Results<Created<PayrollEmployeeIdentifierResponse>, ValidationProblem, ProblemHttpResult>> CreateIdentifier(
        int profileId, CreatePayrollEmployeeIdentifierRequest request, PayrollEmployeeIdentifierService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(profileId, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/payroll-export-profiles/{profileId}/employee-identifiers/{result.Value!.Id}",
                PayrollEmployeeIdentifierResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' creating an employee identifier."),
        };
    }

    private static async Task<Results<Ok<PayrollEmployeeIdentifierResponse>, ValidationProblem, ProblemHttpResult>> UpdateIdentifier(
        int profileId, int id, UpdatePayrollEmployeeIdentifierRequest request, PayrollEmployeeIdentifierService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(profileId, id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayrollEmployeeIdentifierResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' updating an employee identifier."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteIdentifier(
        int profileId, int id, PayrollEmployeeIdentifierService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(profileId, id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' deleting an employee identifier."),
        };
    }
}
