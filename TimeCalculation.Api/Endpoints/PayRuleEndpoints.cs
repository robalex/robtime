using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Endpoints;

public static class PayRuleEndpoints
{
    public static void MapPayRuleEndpoints(this WebApplication app)
    {
        app.MapPost("/payrules", CreatePayRule).WithName("CreatePayRule");
    }

    private static async Task<Results<Created<PayRule>, ValidationProblem, ProblemHttpResult>> CreatePayRule(
        CreatePayRuleRequest request, PayRuleService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created($"/payrules/{result.Value!.Id}", result.Value),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for pay rule creation."),
        };
    }
}
