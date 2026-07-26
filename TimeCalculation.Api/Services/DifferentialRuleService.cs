using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class DifferentialRuleService(PayrollDbContext db)
{
    public async Task<ServiceResult<DifferentialRule>> CreateAsync(CreateDifferentialRuleRequest request, CancellationToken ct)
    {
        var errors = DifferentialRuleRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<DifferentialRule>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<DifferentialRule>.NotFound($"No client with id {request.ClientId}.");
        }

        var rule = new DifferentialRule
        {
            ClientId = request.ClientId,
            Code = request.Code,
            DayScheduleMode = request.DayScheduleMode,
            DaysOfWeek = request.DaysOfWeek ?? [],
            DayOfWeekRangeStart = request.DayOfWeekRangeStart,
            DayOfWeekRangeEnd = request.DayOfWeekRangeEnd,
            SpecificDates = request.SpecificDates ?? [],
            WindowStart = request.WindowStart,
            WindowEnd = request.WindowEnd,
            AdjustmentType = request.AdjustmentType,
            AdjustmentValue = request.AdjustmentValue,
            MinHoursInWindow = request.MinHoursInWindow,
            MinHoursInRange = request.MinHoursInRange,
            ExclusivityGroup = request.ExclusivityGroup,
        };

        var consistencyErrors = DifferentialRuleRequestValidator.ValidateConsistency(rule);
        if (consistencyErrors.Count > 0)
        {
            return ServiceResult<DifferentialRule>.ValidationFailed(consistencyErrors);
        }

        db.DifferentialRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return ServiceResult<DifferentialRule>.Success(rule);
    }

    public async Task<PagedResult<DifferentialRule>> ListAsync(
        int clientId, string? search, PagingQuery paging, CancellationToken ct)
    {
        var query = db.DifferentialRules.Where(d => d.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d => EF.Functions.ILike(d.Code, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.Code)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<DifferentialRule>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<DifferentialRule>> GetAsync(int id, CancellationToken ct)
    {
        var rule = await db.DifferentialRules.FirstOrDefaultAsync(d => d.Id == id, ct);
        return rule is null
            ? ServiceResult<DifferentialRule>.NotFound($"No differential rule with id {id}.")
            : ServiceResult<DifferentialRule>.Success(rule);
    }

    public async Task<ServiceResult<DifferentialRule>> UpdateAsync(int id, UpdateDifferentialRuleRequest request, CancellationToken ct)
    {
        var errors = DifferentialRuleRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<DifferentialRule>.ValidationFailed(errors);
        }

        var rule = await db.DifferentialRules.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (rule is null)
        {
            return ServiceResult<DifferentialRule>.NotFound($"No differential rule with id {id}.");
        }

        rule.Code = request.Code;
        rule.DayScheduleMode = request.DayScheduleMode;
        rule.DaysOfWeek = request.DaysOfWeek ?? [];
        rule.DayOfWeekRangeStart = request.DayOfWeekRangeStart;
        rule.DayOfWeekRangeEnd = request.DayOfWeekRangeEnd;
        rule.SpecificDates = request.SpecificDates ?? [];
        rule.WindowStart = request.WindowStart;
        rule.WindowEnd = request.WindowEnd;
        rule.AdjustmentType = request.AdjustmentType;
        rule.AdjustmentValue = request.AdjustmentValue;
        rule.MinHoursInWindow = request.MinHoursInWindow;
        rule.MinHoursInRange = request.MinHoursInRange;
        rule.ExclusivityGroup = request.ExclusivityGroup;

        var consistencyErrors = DifferentialRuleRequestValidator.ValidateConsistency(rule);
        if (consistencyErrors.Count > 0)
        {
            return ServiceResult<DifferentialRule>.ValidationFailed(consistencyErrors);
        }

        await db.SaveChangesAsync(ct);

        return ServiceResult<DifferentialRule>.Success(rule);
    }

    public async Task<ServiceResult<DifferentialRule>> DeleteAsync(int id, CancellationToken ct)
    {
        var rule = await db.DifferentialRules.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (rule is null)
        {
            return ServiceResult<DifferentialRule>.NotFound($"No differential rule with id {id}.");
        }

        rule.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<DifferentialRule>.Success(rule);
    }
}
