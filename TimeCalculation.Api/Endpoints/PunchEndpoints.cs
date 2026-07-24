using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class PunchEndpoints
{
    public static void MapPunchEndpoints(this WebApplication app)
    {
        app.MapPost("/punches", CreatePunch).WithName("CreatePunch").RequireAuthorization();
    }

    private static async Task<Results<Created<Punch>, ValidationProblem, ProblemHttpResult>> CreatePunch(
        CreatePunchRequest request, ClaimsPrincipal user, PunchService service, CancellationToken ct)
    {
        var createdBy = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.CreateAsync(request, createdBy, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created($"/punches/{result.Value!.Id}", result.Value),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for punch creation."),
        };
    }
}
