using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapPost("/clients", CreateClient).WithName("CreateClient")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapGet("/clients", ListClients).WithName("ListClients")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapGet("/clients/{id:int}", GetClient).WithName("GetClient").RequireAuthorization();
        app.MapPut("/clients/{id:int}", UpdateClient).WithName("UpdateClient")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/clients/{id:int}", DeleteClient).WithName("DeleteClient")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<ClientResponse>, ValidationProblem, ProblemHttpResult>> CreateClient(
        CreateClientRequest request, ClaimsPrincipal user, ClientService service, CancellationToken ct)
    {
        var createdBy = user.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty;
        var result = await service.CreateAsync(request, createdBy, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/clients/{result.Value!.Id}", ClientResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client creation."),
        };
    }

    private static async Task<Ok<PagedResult<ClientResponse>>> ListClients(
        string? search, [AsParameters] PagingQuery paging, ClaimsPrincipal user,
        ClientService service, CancellationToken ct)
    {
        var clients = await service.ListAsync(search, paging, CallerIdentity.FromPrincipal(user).Role, ct);
        return TypedResults.Ok(new PagedResult<ClientResponse>
        {
            Items = clients.Items.Select(ClientResponse.FromEntity).ToList(),
            TotalCount = clients.TotalCount,
            Page = clients.Page,
            PageSize = clients.PageSize,
        });
    }

    private static async Task<Results<Ok<ClientResponse>, ProblemHttpResult>> GetClient(
        int id, ClaimsPrincipal user, ClientService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, CallerIdentity.FromPrincipal(user).Role, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(ClientResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client lookup."),
        };
    }

    private static async Task<Results<Ok<ClientResponse>, ValidationProblem, ProblemHttpResult>> UpdateClient(
        int id, UpdateClientRequest request, ClaimsPrincipal user, ClientService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, CallerIdentity.FromPrincipal(user).Role, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(ClientResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteClient(
        int id, ClaimsPrincipal user, ClientService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, CallerIdentity.FromPrincipal(user).Role, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client deletion."),
        };
    }
}
