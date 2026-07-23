using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PositionEndpoints
{
    public static void MapPositionEndpoints(this WebApplication app)
    {
        app.MapPost("/positions", CreatePosition).WithName("CreatePosition");
        app.MapGet("/positions", ListPositions).WithName("ListPositions");
        app.MapGet("/positions/{id:int}", GetPosition).WithName("GetPosition");
        app.MapPut("/positions/{id:int}", UpdatePosition).WithName("UpdatePosition");
        app.MapDelete("/positions/{id:int}", DeletePosition).WithName("DeletePosition");
    }

    private static async Task<Results<Created<PositionResponse>, ValidationProblem, ProblemHttpResult>> CreatePosition(
        CreatePositionRequest request, PositionService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/positions/{result.Value!.Id}", PositionResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for position creation."),
        };
    }

    private static async Task<Ok<PagedResult<PositionResponse>>> ListPositions(
        int clientId, string? search, [AsParameters] PagingQuery paging, PositionService service, CancellationToken ct)
    {
        var positions = await service.ListAsync(clientId, search, paging, ct);
        return TypedResults.Ok(new PagedResult<PositionResponse>
        {
            Items = positions.Items.Select(PositionResponse.FromEntity).ToList(),
            TotalCount = positions.TotalCount,
            Page = positions.Page,
            PageSize = positions.PageSize,
        });
    }

    private static async Task<Results<Ok<PositionResponse>, ProblemHttpResult>> GetPosition(
        int id, PositionService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PositionResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for position lookup."),
        };
    }

    private static async Task<Results<Ok<PositionResponse>, ValidationProblem, ProblemHttpResult>> UpdatePosition(
        int id, UpdatePositionRequest request, PositionService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PositionResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for position update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePosition(
        int id, PositionService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for position deletion."),
        };
    }
}
