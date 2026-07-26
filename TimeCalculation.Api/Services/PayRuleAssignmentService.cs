using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Effective-dated pay rule assignments for an employee. Orchestrates only: the date rules live in
/// <see cref="PayRuleAssignmentValidator"/> and <see cref="DateRange"/>, which have no database
/// dependency (CLAUDE.md's service/logic split). Mirrors <see cref="PositionAssignmentService"/>.
///
/// Every query goes through the tenant filter — no IgnoreQueryFilters anywhere here — so an
/// assignment belonging to another client is invisible rather than merely forbidden.
/// </summary>
public class PayRuleAssignmentService(PayrollDbContext db)
{
    public async Task<ServiceResult<List<PayRuleAssignmentEntity>>> ListAsync(
        int employeeId, CancellationToken ct)
    {
        var employeeExists = await db.Employees.AnyAsync(e => e.Id == employeeId, ct);
        if (!employeeExists)
        {
            return ServiceResult<List<PayRuleAssignmentEntity>>.NotFound($"No employee with id {employeeId}.");
        }

        var assignments = await db.PayRuleAssignments
            .Include(a => a.PayRule)
            .Where(a => a.EmployeeId == employeeId)
            // Newest first: the current assignment is what you're usually looking for, and an
            // open-ended one sorts to the top under this ordering.
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);

        return ServiceResult<List<PayRuleAssignmentEntity>>.Success(assignments);
    }

    public async Task<ServiceResult<PayRuleAssignmentEntity>> CreateAsync(
        int employeeId, CreatePayRuleAssignmentRequest request, CancellationToken ct)
    {
        var errors = PayRuleAssignmentValidator.ValidateShape(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayRuleAssignmentEntity>.ValidationFailed(errors);
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.NotFound($"No employee with id {employeeId}.");
        }

        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == request.PayRuleId, ct);
        if (payRule is null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.NotFound($"No pay rule with id {request.PayRuleId}.");
        }

        var conflict = await FindConflictAsync(employeeId, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.Conflict(
                $"An employee is governed by only one pay rule at a time. {PayRuleAssignmentValidator.DescribeConflict(conflict)}");
        }

        var assignment = new PayRuleAssignmentEntity
        {
            // Denormalised from the employee, never from the request — same reasoning as
            // PositionAssignmentService: a client-supplied tenant is a claim to verify, not a value
            // to trust.
            ClientId = employee.ClientId,
            EmployeeId = employeeId,
            PayRuleId = request.PayRuleId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        db.PayRuleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        assignment.PayRule = payRule;
        return ServiceResult<PayRuleAssignmentEntity>.Success(assignment);
    }

    public async Task<ServiceResult<PayRuleAssignmentEntity>> UpdateAsync(
        int employeeId, int id, UpdatePayRuleAssignmentRequest request, CancellationToken ct)
    {
        var errors = PayRuleAssignmentValidator.ValidateShape(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayRuleAssignmentEntity>.ValidationFailed(errors);
        }

        var assignment = await db.PayRuleAssignments
            .Include(a => a.PayRule)
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employeeId, ct);
        if (assignment is null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.NotFound($"No assignment with id {id} for employee {employeeId}.");
        }

        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == request.PayRuleId, ct);
        if (payRule is null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.NotFound($"No pay rule with id {request.PayRuleId}.");
        }

        // Excluding itself, or every edit would collide with the row being edited.
        var conflict = await FindConflictAsync(
            employeeId, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.Conflict(
                $"An employee is governed by only one pay rule at a time. {PayRuleAssignmentValidator.DescribeConflict(conflict)}");
        }

        assignment.PayRuleId = request.PayRuleId;
        assignment.EffectiveFrom = request.EffectiveFrom;
        assignment.EffectiveTo = request.EffectiveTo;
        await db.SaveChangesAsync(ct);

        assignment.PayRule = payRule;
        return ServiceResult<PayRuleAssignmentEntity>.Success(assignment);
    }

    public async Task<ServiceResult<PayRuleAssignmentEntity>> DeleteAsync(
        int employeeId, int id, CancellationToken ct)
    {
        var assignment = await db.PayRuleAssignments
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == employeeId, ct);
        if (assignment is null)
        {
            return ServiceResult<PayRuleAssignmentEntity>.NotFound($"No assignment with id {id} for employee {employeeId}.");
        }

        // Hard delete: unlike Client/Employee/Position/PayRule this entity has no IsDeleted column,
        // and an assignment is a statement about a date range rather than a record with its own
        // history. Removing a wrong one should leave no trace that keeps affecting pay resolution.
        db.PayRuleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRuleAssignmentEntity>.Success(assignment);
    }

    private async Task<DateRange?> FindConflictAsync(int employeeId, DateRange proposed, int? excludeId, CancellationToken ct)
    {
        // Fetching the ranges rather than expressing the overlap in SQL keeps the rule in one
        // testable place. An employee's assignment count is small (a career's worth of rule
        // changes), so this is a handful of rows, not a scan.
        var existing = await db.PayRuleAssignments
            .Where(a => a.EmployeeId == employeeId && (excludeId == null || a.Id != excludeId))
            .Select(a => new { a.EffectiveFrom, a.EffectiveTo })
            .ToListAsync(ct);

        return PayRuleAssignmentValidator.FindConflict(
            proposed, existing.Select(a => new DateRange(a.EffectiveFrom, a.EffectiveTo)));
    }
}
