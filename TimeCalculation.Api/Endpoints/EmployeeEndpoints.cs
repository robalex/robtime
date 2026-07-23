using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this WebApplication app)
    {
        app.MapPost("/employees", CreateEmployee).WithName("CreateEmployee");
        app.MapGet("/employees", ListEmployees).WithName("ListEmployees");
        app.MapGet("/employees/{id:int}", GetEmployee).WithName("GetEmployee");
        app.MapPut("/employees/{id:int}", UpdateEmployee).WithName("UpdateEmployee");
        app.MapDelete("/employees/{id:int}", DeleteEmployee).WithName("DeleteEmployee");
    }

    private static async Task<Results<Created<EmployeeResponse>, ValidationProblem, ProblemHttpResult>> CreateEmployee(
        CreateEmployeeRequest request, EmployeeService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/employees/{result.Value!.Id}", EmployeeResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for employee creation."),
        };
    }

    private static async Task<Ok<PagedResult<EmployeeResponse>>> ListEmployees(
        int clientId, string? search, [AsParameters] PagingQuery paging, EmployeeService service, CancellationToken ct)
    {
        var employees = await service.ListAsync(clientId, search, paging, ct);
        return TypedResults.Ok(new PagedResult<EmployeeResponse>
        {
            Items = employees.Items.Select(EmployeeResponse.FromEntity).ToList(),
            TotalCount = employees.TotalCount,
            Page = employees.Page,
            PageSize = employees.PageSize,
        });
    }

    private static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult>> GetEmployee(
        int id, EmployeeService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(EmployeeResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for employee lookup."),
        };
    }

    private static async Task<Results<Ok<EmployeeResponse>, ValidationProblem, ProblemHttpResult>> UpdateEmployee(
        int id, UpdateEmployeeRequest request, EmployeeService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(EmployeeResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for employee update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEmployee(
        int id, EmployeeService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for employee deletion."),
        };
    }
}
