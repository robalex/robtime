using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Earning code mappings nested under a <see cref="PayrollExportProfile"/>. Every query goes
/// through the tenant filter — no IgnoreQueryFilters anywhere here — so a mapping belonging to
/// another client is invisible rather than merely forbidden. The DB carries the real uniqueness
/// guarantee (a partial unique index on ClientId/ProfileId/LineType/LineCode); the conflict check
/// here exists only to turn that into a good error message instead of a raw DbUpdateException.
/// </summary>
public class PayrollEarningCodeMappingService(PayrollDbContext db)
{
    public async Task<ServiceResult<List<PayrollEarningCodeMapping>>> ListAsync(int profileId, CancellationToken ct)
    {
        var profileExists = await db.PayrollExportProfiles.AnyAsync(p => p.Id == profileId, ct);
        if (!profileExists)
        {
            return ServiceResult<List<PayrollEarningCodeMapping>>.NotFound($"No payroll export profile with id {profileId}.");
        }

        var mappings = await db.PayrollEarningCodeMappings
            .Where(m => m.ProfileId == profileId)
            .OrderBy(m => m.LineType)
            .ThenBy(m => m.LineCode)
            .ToListAsync(ct);

        return ServiceResult<List<PayrollEarningCodeMapping>>.Success(mappings);
    }

    public async Task<ServiceResult<PayrollEarningCodeMapping>> CreateAsync(
        int profileId, CreatePayrollEarningCodeMappingRequest request, CancellationToken ct)
    {
        var errors = PayrollEarningCodeMappingRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollEarningCodeMapping>.ValidationFailed(errors);
        }

        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == profileId, ct);
        if (profile is null)
        {
            return ServiceResult<PayrollEarningCodeMapping>.NotFound($"No payroll export profile with id {profileId}.");
        }

        var conflict = await FindConflictAsync(profileId, request.LineType, request.LineCode, excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayrollEarningCodeMapping>.Conflict(DescribeConflict(conflict));
        }

        var mapping = new PayrollEarningCodeMapping
        {
            ClientId = profile.ClientId,
            ProfileId = profileId,
            LineType = request.LineType,
            LineCode = request.LineCode,
            EarningCode = request.EarningCode,
            ValueBasis = request.ValueBasis,
            Description = request.Description ?? string.Empty,
        };

        db.PayrollEarningCodeMappings.Add(mapping);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEarningCodeMapping>.Success(mapping);
    }

    public async Task<ServiceResult<PayrollEarningCodeMapping>> UpdateAsync(
        int profileId, int id, UpdatePayrollEarningCodeMappingRequest request, CancellationToken ct)
    {
        var errors = PayrollEarningCodeMappingRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollEarningCodeMapping>.ValidationFailed(errors);
        }

        var mapping = await db.PayrollEarningCodeMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.ProfileId == profileId, ct);
        if (mapping is null)
        {
            return ServiceResult<PayrollEarningCodeMapping>.NotFound($"No earning code mapping with id {id} for profile {profileId}.");
        }

        var conflict = await FindConflictAsync(profileId, request.LineType, request.LineCode, excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<PayrollEarningCodeMapping>.Conflict(DescribeConflict(conflict));
        }

        mapping.LineType = request.LineType;
        mapping.LineCode = request.LineCode;
        mapping.EarningCode = request.EarningCode;
        mapping.ValueBasis = request.ValueBasis;
        mapping.Description = request.Description ?? string.Empty;

        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEarningCodeMapping>.Success(mapping);
    }

    public async Task<ServiceResult<PayrollEarningCodeMapping>> DeleteAsync(int profileId, int id, CancellationToken ct)
    {
        var mapping = await db.PayrollEarningCodeMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.ProfileId == profileId, ct);
        if (mapping is null)
        {
            return ServiceResult<PayrollEarningCodeMapping>.NotFound($"No earning code mapping with id {id} for profile {profileId}.");
        }

        mapping.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollEarningCodeMapping>.Success(mapping);
    }

    private async Task<PayrollEarningCodeMapping?> FindConflictAsync(
        int profileId, TimeCalculation.Model.PayLineType lineType, string lineCode, int? excludeId, CancellationToken ct)
        => await db.PayrollEarningCodeMappings.FirstOrDefaultAsync(
            m => m.ProfileId == profileId && m.LineType == lineType && m.LineCode == lineCode
                && (excludeId == null || m.Id != excludeId), ct);

    private static string DescribeConflict(PayrollEarningCodeMapping conflict) =>
        $"This profile already maps {conflict.LineType}/'{conflict.LineCode}' to earning code " +
        $"'{conflict.EarningCode}' (mapping id {conflict.Id}). Edit or remove that mapping instead.";
}
