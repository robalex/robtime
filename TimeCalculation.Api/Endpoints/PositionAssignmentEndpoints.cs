using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PositionAssignmentEndpoints
{
    public static void MapPositionAssignmentEndpoints(this WebApplication app)
    {
        // Nested under the employee: an assignment has no meaning apart from the person it's for,
        // and the nesting makes the tenant path explicit in the URL.
        app.MapGet("/employees/{employeeId:int}/positions", ListAssignments)
            .WithName("ListPositionAssignments").RequireAuthorization();
        app.MapPost("/employees/{employeeId:int}/positions", CreateAssignment)
            .WithName("CreatePositionAssignment").RequireAuthorization();
        app.MapPut("/employees/{employeeId:int}/positions/{id:int}", UpdateAssignment)
            .WithName("UpdatePositionAssignment").RequireAuthorization();
        app.MapDelete("/employees/{employeeId:int}/positions/{id:int}", DeleteAssignment)
            .WithName("DeletePositionAssignment").RequireAuthorization();
    }

    private static async Task<Results<Ok<List<PositionAssignmentResponse>>, ProblemHttpResult>> ListAssignments(
        int employeeId, PositionAssignmentService service, CancellationToken ct)
    {
        var result = await service.ListAsync(employeeId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(
                result.Value!.Select(PositionAssignmentResponse.FromEntity).ToList()),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' listing position assignments."),
        };
    }

    private static async Task<Results<Created<PositionAssignmentResponse>, ValidationProblem, ProblemHttpResult>> CreateAssignment(
        int employeeId, CreatePositionAssignmentRequest request, PositionAssignmentService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(employeeId, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/employees/{employeeId}/positions/{result.Value!.Id}",
                PositionAssignmentResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' creating a position assignment."),
        };
    }

    private static async Task<Results<Ok<PositionAssignmentResponse>, ValidationProblem, ProblemHttpResult>> UpdateAssignment(
        int employeeId, int id, UpdatePositionAssignmentRequest request, PositionAssignmentService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(employeeId, id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PositionAssignmentResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' updating a position assignment."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAssignment(
        int employeeId, int id, PositionAssignmentService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(employeeId, id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' deleting a position assignment."),
        };
    }
}
