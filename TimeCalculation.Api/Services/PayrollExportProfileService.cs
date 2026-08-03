using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PayrollExportProfileService(PayrollDbContext db)
{
    public async Task<ServiceResult<PayrollExportProfile>> CreateAsync(
        CreatePayrollExportProfileRequest request, CancellationToken ct)
    {
        var errors = PayrollExportProfileRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollExportProfile>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<PayrollExportProfile>.NotFound($"No client with id {request.ClientId}.");
        }

        var profile = new PayrollExportProfile
        {
            ClientId = request.ClientId,
            Name = request.Name,
            Provider = request.Provider,
            Grouping = request.Grouping ?? PayrollExportGrouping.PayPeriod,
            RoundingPolicy = request.RoundingPolicy ?? PayrollExportRoundingPolicy.DistributeRemainder,
            AdjustmentEarningCode = request.AdjustmentEarningCode ?? string.Empty,
            AmountScale = request.AmountScale ?? 2,
            HoursScale = request.HoursScale ?? 2,
        };

        db.PayrollExportProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollExportProfile>.Success(profile);
    }

    public async Task<PagedResult<PayrollExportProfile>> ListAsync(
        int clientId, PagingQuery paging, CancellationToken ct)
    {
        var query = db.PayrollExportProfiles.Where(p => p.ClientId == clientId);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<PayrollExportProfile>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<PayrollExportProfile>> GetAsync(int id, CancellationToken ct)
    {
        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        return profile is null
            ? ServiceResult<PayrollExportProfile>.NotFound($"No payroll export profile with id {id}.")
            : ServiceResult<PayrollExportProfile>.Success(profile);
    }

    public async Task<ServiceResult<PayrollExportProfile>> UpdateAsync(
        int id, UpdatePayrollExportProfileRequest request, CancellationToken ct)
    {
        var errors = PayrollExportProfileRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<PayrollExportProfile>.ValidationFailed(errors);
        }

        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return ServiceResult<PayrollExportProfile>.NotFound($"No payroll export profile with id {id}.");
        }

        profile.Name = request.Name;
        profile.Provider = request.Provider;
        profile.Grouping = request.Grouping ?? PayrollExportGrouping.PayPeriod;
        profile.RoundingPolicy = request.RoundingPolicy ?? PayrollExportRoundingPolicy.DistributeRemainder;
        profile.AdjustmentEarningCode = request.AdjustmentEarningCode ?? string.Empty;
        profile.AmountScale = request.AmountScale ?? 2;
        profile.HoursScale = request.HoursScale ?? 2;

        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollExportProfile>.Success(profile);
    }

    public async Task<ServiceResult<PayrollExportProfile>> DeleteAsync(int id, CancellationToken ct)
    {
        var profile = await db.PayrollExportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return ServiceResult<PayrollExportProfile>.NotFound($"No payroll export profile with id {id}.");
        }

        var mappingCount = await db.PayrollEarningCodeMappings.CountAsync(m => m.ProfileId == id, ct);
        var identifierCount = await db.PayrollEmployeeIdentifiers.CountAsync(i => i.ProfileId == id, ct);
        if (mappingCount > 0 || identifierCount > 0)
        {
            return ServiceResult<PayrollExportProfile>.Conflict(
                $"This profile still has {mappingCount} earning code mapping(s) and " +
                $"{identifierCount} employee identifier(s). Remove those first.");
        }

        profile.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayrollExportProfile>.Success(profile);
    }
}
