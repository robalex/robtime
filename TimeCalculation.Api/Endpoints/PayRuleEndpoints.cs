using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class PayRuleEndpoints
{
    public static void MapPayRuleEndpoints(this WebApplication app)
    {
        app.MapPost("/payrules", CreatePayRule).WithName("CreatePayRule");
        app.MapGet("/payrules", ListPayRules).WithName("ListPayRules");
        app.MapGet("/payrules/{id:int}", GetPayRule).WithName("GetPayRule");
        app.MapPut("/payrules/{id:int}", UpdatePayRule).WithName("UpdatePayRule");
        app.MapDelete("/payrules/{id:int}", DeletePayRule).WithName("DeletePayRule");
    }

    private static async Task<Results<Created<PayRuleResponse>, ValidationProblem, ProblemHttpResult>> CreatePayRule(
        CreatePayRuleRequest request, PayRuleService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/payrules/{result.Value!.Id}", PayRuleResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for pay rule creation."),
        };
    }

    private static async Task<Ok<PagedResult<PayRuleResponse>>> ListPayRules(
        int clientId, PayRuleStatus? status, [AsParameters] PagingQuery paging,
        PayRuleService service, CancellationToken ct)
    {
        var payRules = await service.ListAsync(clientId, status, paging, ct);
        return TypedResults.Ok(new PagedResult<PayRuleResponse>
        {
            Items = payRules.Items.Select(PayRuleResponse.FromEntity).ToList(),
            TotalCount = payRules.TotalCount,
            Page = payRules.Page,
            PageSize = payRules.PageSize,
        });
    }

    private static async Task<Results<Ok<PayRuleResponse>, ProblemHttpResult>> GetPayRule(
        int id, PayRuleService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayRuleResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for pay rule lookup."),
        };
    }

    private static async Task<Results<Ok<PayRuleResponse>, ValidationProblem, ProblemHttpResult>> UpdatePayRule(
        int id, UpdatePayRuleRequest request, PayRuleService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayRuleResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for pay rule update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePayRule(
        int id, PayRuleService service, CancellationToken ct)
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
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for pay rule deletion."),
        };
    }
}
