using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this WebApplication app)
    {
        app.MapPost("/employees", CreateEmployee).WithName("CreateEmployee");
    }

    private static async Task<Results<Created<Employee>, ValidationProblem, ProblemHttpResult>> CreateEmployee(
        CreateEmployeeRequest request, PayrollDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName)) errors["firstName"] = ["First name is required."];
        if (string.IsNullOrWhiteSpace(request.LastName)) errors["lastName"] = ["Last name is required."];
        if (request.MinimumWage < 0) errors["minimumWage"] = ["Minimum wage cannot be negative."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
            return TypedResults.Problem(
                detail: $"No client with id {request.ClientId}.", statusCode: StatusCodes.Status404NotFound);

        var employee = new Employee
        {
            ClientId = request.ClientId,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName ?? string.Empty,
            LastName = request.LastName,
            Salutation = request.Salutation ?? string.Empty,
            PostNominalLetters = request.PostNominalLetters ?? string.Empty,
            MinimumWage = request.MinimumWage,
            HomeTimeZoneId = request.HomeTimeZoneId ?? "America/New_York",
            State = request.State ?? string.Empty,
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/employees/{employee.Id}", employee);
    }
}
