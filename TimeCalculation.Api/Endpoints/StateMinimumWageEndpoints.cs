using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

/// <summary>Shared reference data, not client-owned — every route is SystemAdmin-only, matching
/// ClientEndpoints' cross-tenant list/create gating.</summary>
public static class StateMinimumWageEndpoints
{
    public static void MapStateMinimumWageEndpoints(this WebApplication app)
    {
        app.MapPost("/state-minimum-wages", CreateStateMinimumWage).WithName("CreateStateMinimumWage")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapGet("/state-minimum-wages", ListStateMinimumWages).WithName("ListStateMinimumWages")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapGet("/state-minimum-wages/{id:int}", GetStateMinimumWage).WithName("GetStateMinimumWage")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapPut("/state-minimum-wages/{id:int}", UpdateStateMinimumWage).WithName("UpdateStateMinimumWage")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
        app.MapDelete("/state-minimum-wages/{id:int}", DeleteStateMinimumWage).WithName("DeleteStateMinimumWage")
            .RequireAuthorization(AuthorizationPolicies.SystemAdmin);
    }

    private static async Task<Results<Created<StateMinimumWageResponse>, ValidationProblem, ProblemHttpResult>> CreateStateMinimumWage(
        CreateStateMinimumWageRequest request, StateMinimumWageService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/state-minimum-wages/{result.Value!.Id}", StateMinimumWageResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for state minimum wage creation."),
        };
    }

    private static async Task<Ok<PagedResult<StateMinimumWageResponse>>> ListStateMinimumWages(
        string? state, [AsParameters] PagingQuery paging, StateMinimumWageService service, CancellationToken ct)
    {
        var wages = await service.ListAsync(state, paging, ct);
        return TypedResults.Ok(new PagedResult<StateMinimumWageResponse>
        {
            Items = wages.Items.Select(StateMinimumWageResponse.FromEntity).ToList(),
            TotalCount = wages.TotalCount,
            Page = wages.Page,
            PageSize = wages.PageSize,
        });
    }

    private static async Task<Results<Ok<StateMinimumWageResponse>, ProblemHttpResult>> GetStateMinimumWage(
        int id, StateMinimumWageService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(StateMinimumWageResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for state minimum wage lookup."),
        };
    }

    private static async Task<Results<Ok<StateMinimumWageResponse>, ValidationProblem, ProblemHttpResult>> UpdateStateMinimumWage(
        int id, UpdateStateMinimumWageRequest request, StateMinimumWageService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(StateMinimumWageResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for state minimum wage update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteStateMinimumWage(
        int id, StateMinimumWageService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for state minimum wage deletion."),
        };
    }
}
