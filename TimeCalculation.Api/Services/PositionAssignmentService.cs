using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Effective-dated position assignments for an employee. Orchestrates only: the date rules live in
/// <see cref="PositionAssignmentValidator"/> and <see cref="DateRange"/>, which have no database
/// dependency (CLAUDE.md's service/logic split).
///
/// Every query goes through the tenant filter — no IgnoreQueryFilters anywhere here — so an
/// assignment belonging to another client is invisible rather than merely forbidden.
/// </summary>
public class PositionAssignmentService(PayrollDbContext db)
{
    public async Task<ServiceResult<List<EmployeePositionAssignmentEntity>>> ListAsync(
        int employeeId, CancellationToken ct)
    {
        var employeeExists = await db.Employees.AnyAsync(e => e.Id == employeeId, ct);
        if (!employeeExists)
        {
            return ServiceResult<List<EmployeePositionAssignmentEntity>>.NotFound($"No employee with id {employeeId}.");
        }

        var assignments = await db.EmployeePositionAssignments
            .Include(a => a.Position)
            .Where(a => a.EmployeeId == employeeId)
            // Newest first: the current assignment is what you're usually looking for, and an
            // open-ended one sorts to the top under this ordering.
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);

        return ServiceResult<List<EmployeePositionAssignmentEntity>>.Success(assignments);
    }

    public async Task<ServiceResult<EmployeePositionAssignmentEntity>> CreateAsync(
        int employeeId, CreatePositionAssignmentRequest request, CancellationToken ct)
    {
        var errors = PositionAssignmentValidator.ValidateShape(request);
        if (errors.Count > 0)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.ValidationFailed(errors);
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.NotFound($"No employee with id {employeeId}.");
        }

        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == request.PositionId, ct);
        if (position is null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.NotFound($"No position with id {request.PositionId}.");
        }

        var conflict = await FindConflictAsync(employeeId, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.Conflict(
                $"An employee can hold only one position at a time. {PositionAssignmentValidator.DescribeConflict(conflict)}");
        }

        var assignment = new EmployeePositionAssignmentEntity
        {
            // Denormalised from the employee, never from the request — same reasoning as
            // PunchService deriving ClientId server-side: a client-supplied tenant is a claim to
            // verify, not a value to trust.
            ClientId = employee.ClientId,
            EmployeeId = employeeId,
            PositionId = request.PositionId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Rate = request.Rate,
        };

        db.EmployeePositionAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        assignment.Position = position;
        return ServiceResult<EmployeePositionAssignmentEntity>.Success(assignment);
    }

    public async Task<ServiceResult<EmployeePositionAssignmentEntity>> UpdateAsync(
        int employeeId, int id, UpdatePositionAssignmentRequest request, CancellationToken ct)
    {
        var errors = PositionAssignmentValidator.ValidateShape(request);
        if (errors.Count > 0)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.ValidationFailed(errors);
        }

        var assignment = await db.EmployeePositionAssignments
            .Include(a => a.Position)
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employeeId, ct);
        if (assignment is null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.NotFound($"No assignment with id {id} for employee {employeeId}.");
        }

        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == request.PositionId, ct);
        if (position is null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.NotFound($"No position with id {request.PositionId}.");
        }

        // Excluding itself, or every edit would collide with the row being edited.
        var conflict = await FindConflictAsync(
            employeeId, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.Conflict(
                $"An employee can hold only one position at a time. {PositionAssignmentValidator.DescribeConflict(conflict)}");
        }

        assignment.PositionId = request.PositionId;
        assignment.EffectiveFrom = request.EffectiveFrom;
        assignment.EffectiveTo = request.EffectiveTo;
        assignment.Rate = request.Rate;
        await db.SaveChangesAsync(ct);

        assignment.Position = position;
        return ServiceResult<EmployeePositionAssignmentEntity>.Success(assignment);
    }

    public async Task<ServiceResult<EmployeePositionAssignmentEntity>> DeleteAsync(
        int employeeId, int id, CancellationToken ct)
    {
        var assignment = await db.EmployeePositionAssignments
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employeeId, ct);
        if (assignment is null)
        {
            return ServiceResult<EmployeePositionAssignmentEntity>.NotFound($"No assignment with id {id} for employee {employeeId}.");
        }

        // Hard delete: unlike Client/Employee/Position this entity has no IsDeleted column, and an
        // assignment is a statement about a date range rather than a record with its own history.
        // Removing a wrong one should leave no trace that keeps affecting pay resolution.
        db.EmployeePositionAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);

        return ServiceResult<EmployeePositionAssignmentEntity>.Success(assignment);
    }

    private async Task<DateRange?> FindConflictAsync(int employeeId, DateRange proposed, int? excludeId, CancellationToken ct)
    {
        // Fetching the ranges rather than expressing the overlap in SQL keeps the rule in one
        // testable place. An employee's assignment count is small (a career's worth of role changes),
        // so this is a handful of rows, not a scan.
        var existing = await db.EmployeePositionAssignments
            .Where(a => a.EmployeeId == employeeId && (excludeId == null || a.Id != excludeId))
            .Select(a => new { a.EffectiveFrom, a.EffectiveTo })
            .ToListAsync(ct);

        return PositionAssignmentValidator.FindConflict(
            proposed, existing.Select(a => new DateRange(a.EffectiveFrom, a.EffectiveTo)));
    }
}
