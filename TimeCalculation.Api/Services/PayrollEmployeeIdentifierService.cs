using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Employee identifiers nested under a <see cref="PayrollExportProfile"/>. Two uniqueness guarantees
/// matter here, both DB-enforced (partial unique indexes) and mirrored here as a proactive check for
/// a good error message: one identifier per employee per profile, and — the one that actually
/// protects a paycheck — at most one employee per provider id within a profile, so two RobTime
/// employees can never silently merge into one payroll payment.
/// </summary>
public class PayrollEmployeeIdentifierService(PayrollDbContext db)
{
    public async Task<ServiceResult<List<PayrollEmployeeIdentifier>>> ListAsync(int profileId, CancellationToken ct)
    {
        var profileExists = await db.PayrollExportProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
        {
            return ServiceResult<List<PayrollEmployeeIdentifier>>.NotFound($"No payroll export profile with id {profileId}.");
        }

        var identifiers = await db.PayrollEmployeeIdentifiers
            .Where(i => i.ProfileId == profileId)
            .OrderBy(i => i.ExternalEmployeeId)
            .ToListAsync(ct);

        return ServiceResult<List<PayrollEmployeeIdentifier>>.Success(identifiers);
    }

    public async Task<ServiceResult<PayrollEmployeeIdentifier>> CreateAsync(
        int profileId, CreatePayrollEmployeeIdentifierRequest request, CancellationToken ct)
    {
        var errors = PayrollEmployeeIdentifierRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.ValidationFailed(errors);
        }

        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.NotFound($"No payroll export profile with id {profileId}.");
        }

        // The employee's own tenant filter already keeps a cross-tenant id from resolving at all;
        // this re-check against the profile's own ClientId is defense in depth, same reasoning
        // PayRuleService gives for re-verifying a HolidayCalendarId belongs to the request's client.
        var employee = await db.Employees.FirstOrDefaultAsync(
            e => e.Id == request.EmployeeId && e.ClientId == profile.ClientId, ct);
        if (employee is null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.NotFound(
                $"No employee with id {request.EmployeeId} for this client.");
        }

        var externalId = request.ExternalEmployeeId.Trim();

        var conflict = await FindConflictAsync(profileId, request.EmployeeId, externalId, excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.Conflict(DescribeConflict(conflict, request.EmployeeId, externalId));
        }

        var identifier = new PayrollEmployeeIdentifier
        {
            ClientId = profile.ClientId,
            ProfileId = profileId,
            EmployeeId = request.EmployeeId,
            ExternalEmployeeId = externalId,
        };

        db.PayrollEmployeeIdentifiers.Add(identifier);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEmployeeIdentifier>.Success(identifier);
    }

    public async Task<ServiceResult<PayrollEmployeeIdentifier>> UpdateAsync(
        int profileId, int id, UpdatePayrollEmployeeIdentifierRequest request, CancellationToken ct)
    {
        var errors = PayrollEmployeeIdentifierRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.ValidationFailed(errors);
        }

        var identifier = await db.PayrollEmployeeIdentifiers
            .FirstOrDefaultAsync(i => i.Id == id && i.ProfileId == profileId, ct);
        if (identifier is null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.NotFound($"No employee identifier with id {id} for profile {profileId}.");
        }

        var externalId = request.ExternalEmployeeId.Trim();

        var conflict = await FindConflictAsync(profileId, identifier.EmployeeId, externalId, excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.Conflict(DescribeConflict(conflict, identifier.EmployeeId, externalId));
        }

        identifier.ExternalEmployeeId = externalId;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEmployeeIdentifier>.Success(identifier);
    }

    public async Task<ServiceResult<PayrollEmployeeIdentifier>> DeleteAsync(int profileId, int id, CancellationToken ct)
    {
        var identifier = await db.PayrollEmployeeIdentifiers
            .FirstOrDefaultAsync(i => i.Id == id && i.ProfileId == profileId, ct);
        if (identifier is null)
        {
            return ServiceResult<PayrollEmployeeIdentifier>.NotFound($"No employee identifier with id {id} for profile {profileId}.");
        }

        identifier.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEmployeeIdentifier>.Success(identifier);
    }

    private async Task<PayrollEmployeeIdentifier?> FindConflictAsync(
        int profileId, int employeeId, string externalEmployeeId, int? excludeId, CancellationToken ct)
        => await db.PayrollEmployeeIdentifiers.FirstOrDefaultAsync(
            i => i.ProfileId == profileId
                && (i.EmployeeId == employeeId || i.ExternalEmployeeId == externalEmployeeId)
                && (excludeId == null || i.Id != excludeId), ct);

    private static string DescribeConflict(PayrollEmployeeIdentifier conflict, int employeeId, string externalEmployeeId) =>
        conflict.EmployeeId == employeeId
            ? $"Employee {employeeId} already has an identifier on this profile ('{conflict.ExternalEmployeeId}', id {conflict.Id})."
            : $"'{externalEmployeeId}' is already mapped to employee {conflict.EmployeeId} on this profile (id {conflict.Id}) — " +
              "two employees cannot share one payroll identifier.";
}
