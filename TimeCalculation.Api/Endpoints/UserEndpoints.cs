using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapPost("/users", CreateUser).WithName("CreateUser")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<UserResponse>, ValidationProblem, ProblemHttpResult>> CreateUser(
        CreateUserRequest request, ClaimsPrincipal user, UserProvisioningService service, CancellationToken ct)
    {
        var callerRole = Enum.Parse<AppRole>(user.FindFirst(TenantClaimTypes.Role)!.Value);
        var callerClientId = user.FindFirst(TenantClaimTypes.ClientId) is { } claim ? int.Parse(claim.Value) : (int?)null;

        var result = await service.CreateAsync(request, callerRole, callerClientId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/users/{result.Value!.CognitoSub}", UserResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Forbidden => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for user creation."),
        };
    }
}
