using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class DifferentialSandboxEndpoints
{
    public static void MapDifferentialSandboxEndpoints(this WebApplication app)
    {
        // ClientAdmin, matching can.manageDifferentialRules on the frontend — this is a setup-time
        // diagnostic for configuring differentials, not a payroll-facing read.
        app.MapPost("/differentials/sandbox", RunSandbox).WithName("RunDifferentialSandbox")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Ok<DifferentialSandboxResponse>, ValidationProblem, ProblemHttpResult>> RunSandbox(
        DifferentialSandboxRequest request, DifferentialSandboxService service, CancellationToken ct)
    {
        var result = await service.RunAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(result.Value!),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' running the differential sandbox."),
        };
    }
}
