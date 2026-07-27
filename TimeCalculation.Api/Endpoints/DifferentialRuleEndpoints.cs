using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class DifferentialRuleEndpoints
{
    public static void MapDifferentialRuleEndpoints(this WebApplication app)
    {
        app.MapPost("/differentialrules", CreateDifferentialRule).WithName("CreateDifferentialRule")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapGet("/differentialrules", ListDifferentialRules).WithName("ListDifferentialRules").RequireAuthorization();
        app.MapGet("/differentialrules/{id:int}", GetDifferentialRule).WithName("GetDifferentialRule").RequireAuthorization();
        app.MapPut("/differentialrules/{id:int}", UpdateDifferentialRule).WithName("UpdateDifferentialRule")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/differentialrules/{id:int}", DeleteDifferentialRule).WithName("DeleteDifferentialRule")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<DifferentialRuleResponse>, ValidationProblem, ProblemHttpResult>> CreateDifferentialRule(
        CreateDifferentialRuleRequest request, DifferentialRuleService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/differentialrules/{result.Value!.Id}", DifferentialRuleResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for differential rule creation."),
        };
    }

    private static async Task<Ok<PagedResult<DifferentialRuleResponse>>> ListDifferentialRules(
        int clientId, string? search, [AsParameters] PagingQuery paging, DifferentialRuleService service, CancellationToken ct)
    {
        var rules = await service.ListAsync(clientId, search, paging, ct);
        return TypedResults.Ok(new PagedResult<DifferentialRuleResponse>
        {
            Items = rules.Items.Select(DifferentialRuleResponse.FromEntity).ToList(),
            TotalCount = rules.TotalCount,
            Page = rules.Page,
            PageSize = rules.PageSize,
        });
    }

    private static async Task<Results<Ok<DifferentialRuleResponse>, ProblemHttpResult>> GetDifferentialRule(
        int id, DifferentialRuleService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(DifferentialRuleResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for differential rule lookup."),
        };
    }

    private static async Task<Results<Ok<DifferentialRuleResponse>, ValidationProblem, ProblemHttpResult>> UpdateDifferentialRule(
        int id, UpdateDifferentialRuleRequest request, DifferentialRuleService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(DifferentialRuleResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for differential rule update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDifferentialRule(
        int id, DifferentialRuleService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for differential rule deletion."),
        };
    }
}
