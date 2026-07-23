using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapPost("/clients", CreateClient).WithName("CreateClient");
        app.MapGet("/clients", ListClients).WithName("ListClients");
        app.MapGet("/clients/{id:int}", GetClient).WithName("GetClient");
        app.MapPut("/clients/{id:int}", UpdateClient).WithName("UpdateClient");
        app.MapDelete("/clients/{id:int}", DeleteClient).WithName("DeleteClient");
    }

    private static async Task<Results<Created<ClientResponse>, ValidationProblem>> CreateClient(
        CreateClientRequest request, ClientService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/clients/{result.Value!.Id}", ClientResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client creation."),
        };
    }

    private static async Task<Ok<PagedResult<ClientResponse>>> ListClients(
        string? search, [AsParameters] PagingQuery paging, ClientService service, CancellationToken ct)
    {
        var clients = await service.ListAsync(search, paging, ct);
        return TypedResults.Ok(new PagedResult<ClientResponse>
        {
            Items = clients.Items.Select(ClientResponse.FromEntity).ToList(),
            TotalCount = clients.TotalCount,
            Page = clients.Page,
            PageSize = clients.PageSize,
        });
    }

    private static async Task<Results<Ok<ClientResponse>, ProblemHttpResult>> GetClient(
        int id, ClientService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
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
        int id, UpdateClientRequest request, ClientService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
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
        int id, ClientService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
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
