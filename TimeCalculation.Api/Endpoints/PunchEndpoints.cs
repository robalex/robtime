using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Endpoints;

public static class PunchEndpoints
{
    public static void MapPunchEndpoints(this WebApplication app)
    {
        app.MapPost("/punches", CreatePunch).WithName("CreatePunch");
    }

    private static async Task<Results<Created<Punch>, ValidationProblem, ProblemHttpResult>> CreatePunch(
        CreatePunchRequest request, PayrollDbContext db, IClock clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Kind == PunchKind.FixedDollar && request.Amount is null)
            errors["amount"] = ["Amount is required for FixedDollar punches."];
        if (request.Kind == PunchKind.FixedHours && request.Hours is null)
            errors["hours"] = ["Hours is required for FixedHours punches."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var employeeExists = await db.Employees.AnyAsync(e => e.Id == request.EmployeeId, ct);
        if (!employeeExists)
            return TypedResults.Problem(
                detail: $"No employee with id {request.EmployeeId}.", statusCode: StatusCodes.Status404NotFound);

        if (request.PositionId is { } positionId)
        {
            var positionExists = await db.Positions.AnyAsync(p => p.Id == positionId, ct);
            if (!positionExists)
                return TypedResults.Problem(
                    detail: $"No position with id {positionId}.", statusCode: StatusCodes.Status404NotFound);
        }

        var punch = new Punch
        {
            EmployeeId = request.EmployeeId,
            PunchTime = request.PunchTime,
            PunchTimeZoneId = request.PunchTimeZoneId ?? "UTC",
            Kind = request.Kind,
            Subtype = request.Subtype,
            PositionId = request.PositionId,
            Amount = request.Amount,
            Hours = request.Hours,
            BonusKind = request.BonusKind,
            CountsTowardRegularRate = request.CountsTowardRegularRate,
            CreatedAt = clock.GetCurrentInstant(),
            CreatedBy = request.CreatedBy,
            DeviceId = request.DeviceId,
            DevicePunchId = request.DevicePunchId,
        };

        db.Punches.Add(punch);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (request.DeviceId is not null && request.DevicePunchId is not null)
        {
            return TypedResults.Problem(
                detail: $"A punch from device {request.DeviceId} with device punch id {request.DevicePunchId} already exists for employee {request.EmployeeId}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/punches/{punch.Id}", punch);
    }
}
