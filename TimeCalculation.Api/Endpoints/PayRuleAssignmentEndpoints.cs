using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class PayRuleAssignmentEndpoints
{
    public static void MapPayRuleAssignmentEndpoints(this WebApplication app)
    {
        // Nested under the employee, same shape as PositionAssignmentEndpoints: an assignment has no
        // meaning apart from the person it's for, and the nesting makes the tenant path explicit.
        app.MapGet("/employees/{employeeId:int}/payrules", ListAssignments)
            .WithName("ListPayRuleAssignments").RequireAuthorization();
        app.MapPost("/employees/{employeeId:int}/payrules", CreateAssignment)
            .WithName("CreatePayRuleAssignment").RequireAuthorization();
        app.MapPut("/employees/{employeeId:int}/payrules/{id:int}", UpdateAssignment)
            .WithName("UpdatePayRuleAssignment").RequireAuthorization();
        app.MapDelete("/employees/{employeeId:int}/payrules/{id:int}", DeleteAssignment)
            .WithName("DeletePayRuleAssignment").RequireAuthorization();
    }

    private static async Task<Results<Ok<List<PayRuleAssignmentResponse>>, ProblemHttpResult>> ListAssignments(
        int employeeId, PayRuleAssignmentService service, CancellationToken ct)
    {
        var result = await service.ListAsync(employeeId, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(
                result.Value!.Select(PayRuleAssignmentResponse.FromEntity).ToList()),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' listing pay rule assignments."),
        };
    }

    private static async Task<Results<Created<PayRuleAssignmentResponse>, ValidationProblem, ProblemHttpResult>> CreateAssignment(
        int employeeId, CreatePayRuleAssignmentRequest request, PayRuleAssignmentService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(employeeId, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/employees/{employeeId}/payrules/{result.Value!.Id}",
                PayRuleAssignmentResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' creating a pay rule assignment."),
        };
    }

    private static async Task<Results<Ok<PayRuleAssignmentResponse>, ValidationProblem, ProblemHttpResult>> UpdateAssignment(
        int employeeId, int id, UpdatePayRuleAssignmentRequest request, PayRuleAssignmentService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(employeeId, id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(PayRuleAssignmentResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            ServiceResultKind.Conflict => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' updating a pay rule assignment."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAssignment(
        int employeeId, int id, PayRuleAssignmentService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(employeeId, id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' deleting a pay rule assignment."),
        };
    }
}
