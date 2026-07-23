using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this WebApplication app)
    {
        app.MapPost("/employees", CreateEmployee).WithName("CreateEmployee");
    }

    private static async Task<Results<Created<Employee>, ValidationProblem, ProblemHttpResult>> CreateEmployee(
        CreateEmployeeRequest request, EmployeeService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created($"/employees/{result.Value!.Id}", result.Value),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for employee creation."),
        };
    }
}
