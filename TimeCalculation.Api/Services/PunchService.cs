using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PunchService(PayrollDbContext db, IClock clock)
{
    public async Task<ServiceResult<Punch>> CreateAsync(CreatePunchRequest request, CancellationToken ct)
    {
        var errors = PunchRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Punch>.ValidationFailed(errors);
        }

        // Fetched (not just AnyAsync) because the punch needs the employee's ClientId — the
        // request only carries EmployeeId, and deriving ClientId server-side from the employee
        // record avoids trusting a client-supplied ClientId that might not actually match.
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null)
        {
            return ServiceResult<Punch>.NotFound($"No employee with id {request.EmployeeId}.");
        }

        if (request.PositionId is { } positionId)
        {
            var positionExists = await db.Positions.AnyAsync(p => p.Id == positionId, ct);
            if (!positionExists)
            {
                return ServiceResult<Punch>.NotFound($"No position with id {positionId}.");
            }
        }

        var punch = new Punch
        {
            ClientId = employee.ClientId,
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
            return ServiceResult<Punch>.Conflict(
                $"A punch from device {request.DeviceId} with device punch id {request.DevicePunchId} already exists for employee {request.EmployeeId}.");
        }

        return ServiceResult<Punch>.Success(punch);
    }
}
