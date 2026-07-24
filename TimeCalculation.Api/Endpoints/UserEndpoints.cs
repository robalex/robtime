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
        // The ClientAdmin policy on this route guarantees a parseable custom:role claim, so the
        // null-coalesce below is unreachable in practice — but CallerIdentity models the claim as
        // optional (see its doc comment) rather than asserting it, so this states the fallback
        // explicitly instead of relying on a null-forgiving `!`.
        var caller = CallerIdentity.FromPrincipal(user);
        var result = await service.CreateAsync(request, caller.Role ?? AppRole.Employee, caller.ClientId, ct);
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
