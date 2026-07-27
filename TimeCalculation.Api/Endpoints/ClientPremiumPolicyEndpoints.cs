using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class ClientPremiumPolicyEndpoints
{
    public static void MapClientPremiumPolicyEndpoints(this WebApplication app)
    {
        app.MapPost("/clientpremiumpolicies", CreateClientPremiumPolicy).WithName("CreateClientPremiumPolicy").RequireAuthorization();
        app.MapGet("/clientpremiumpolicies", ListClientPremiumPolicies).WithName("ListClientPremiumPolicies").RequireAuthorization();
        app.MapGet("/clientpremiumpolicies/{id:int}", GetClientPremiumPolicy).WithName("GetClientPremiumPolicy").RequireAuthorization();
        app.MapPut("/clientpremiumpolicies/{id:int}", UpdateClientPremiumPolicy).WithName("UpdateClientPremiumPolicy").RequireAuthorization();
        app.MapDelete("/clientpremiumpolicies/{id:int}", DeleteClientPremiumPolicy).WithName("DeleteClientPremiumPolicy").RequireAuthorization();
    }

    private static async Task<Results<Created<ClientPremiumPolicyResponse>, ValidationProblem, ProblemHttpResult>> CreateClientPremiumPolicy(
        CreateClientPremiumPolicyRequest request, ClaimsPrincipal user, ClientPremiumPolicyService service, CancellationToken ct)
    {
        var setBy = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.CreateAsync(request, setBy, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/clientpremiumpolicies/{result.Value!.Id}", ClientPremiumPolicyResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client premium policy creation."),
        };
    }

    private static async Task<Ok<PagedResult<ClientPremiumPolicyResponse>>> ListClientPremiumPolicies(
        int clientId, string? premiumCode, [AsParameters] PagingQuery paging, ClientPremiumPolicyService service, CancellationToken ct)
    {
        var policies = await service.ListAsync(clientId, premiumCode, paging, ct);
        return TypedResults.Ok(new PagedResult<ClientPremiumPolicyResponse>
        {
            Items = policies.Items.Select(ClientPremiumPolicyResponse.FromEntity).ToList(),
            TotalCount = policies.TotalCount,
            Page = policies.Page,
            PageSize = policies.PageSize,
        });
    }

    private static async Task<Results<Ok<ClientPremiumPolicyResponse>, ProblemHttpResult>> GetClientPremiumPolicy(
        int id, ClientPremiumPolicyService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(ClientPremiumPolicyResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client premium policy lookup."),
        };
    }

    private static async Task<Results<Ok<ClientPremiumPolicyResponse>, ValidationProblem, ProblemHttpResult>> UpdateClientPremiumPolicy(
        int id, UpdateClientPremiumPolicyRequest request, ClaimsPrincipal user, ClientPremiumPolicyService service, CancellationToken ct)
    {
        var setBy = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.UpdateAsync(id, request, setBy, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(ClientPremiumPolicyResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client premium policy update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteClientPremiumPolicy(
        int id, ClientPremiumPolicyService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client premium policy deletion."),
        };
    }
}
