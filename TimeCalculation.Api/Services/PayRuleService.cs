using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PayRuleService(PayrollDbContext db)
{
    public async Task<ServiceResult<PayRule>> CreateAsync(CreatePayRuleRequest request, CancellationToken ct)
    {
        var requestErrors = PayRuleRequestValidator.Validate(request);
        if (requestErrors.Count > 0)
        {
            return ServiceResult<PayRule>.ValidationFailed(requestErrors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<PayRule>.NotFound($"No client with id {request.ClientId}.");
        }

        var payRule = PayRuleRequestMapper.BuildFromRequest(request);

        var consistencyErrors = PayRuleRequestValidator.ValidateConsistency(payRule);
        if (consistencyErrors.Count > 0)
        {
            return ServiceResult<PayRule>.ValidationFailed(consistencyErrors);
        }

        db.PayRules.Add(payRule);
        await db.SaveChangesAsync(ct);

        // RuleFamilyId is the stable identity across a rule's edit history (Gap F); by convention
        // it equals the first version's own Id, which only exists once the row above is saved and
        // EF has populated it. A second save is the only way to close that chicken-and-egg gap.
        payRule.RuleFamilyId = payRule.Id;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(payRule);
    }

    public async Task<PagedResult<PayRule>> ListAsync(
        int clientId, PayRuleStatus? status, PagingQuery paging, CancellationToken ct)
    {
        var query = db.PayRules.Where(r => r.ClientId == clientId);
        if (status is { } statusFilter)
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(r => r.Name).ThenBy(r => r.Version)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<PayRule>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<PayRule>> GetAsync(int id, CancellationToken ct)
    {
        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        return payRule is null
            ? ServiceResult<PayRule>.NotFound($"No pay rule with id {id}.")
            : ServiceResult<PayRule>.Success(payRule);
    }

    public async Task<ServiceResult<PayRule>> UpdateAsync(int id, UpdatePayRuleRequest request, CancellationToken ct)
    {
        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (payRule is null)
        {
            return ServiceResult<PayRule>.NotFound($"No pay rule with id {id}.");
        }

        if (!PayRuleRequestValidator.IsMutable(payRule))
        {
            return ServiceResult<PayRule>.Conflict(
                $"Pay rule {id} is {payRule.Status} and can no longer be edited directly. " +
                "Active/Superseded rules are never mutated in place (Gap F) — creating a new version " +
                "is Phase 4 UI work, not yet available.");
        }

        PayRuleRequestMapper.ApplyUpdate(payRule, request);

        var consistencyErrors = PayRuleRequestValidator.ValidateConsistency(payRule);
        if (consistencyErrors.Count > 0)
        {
            return ServiceResult<PayRule>.ValidationFailed(consistencyErrors);
        }

        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(payRule);
    }

    public async Task<ServiceResult<PayRule>> DeleteAsync(int id, CancellationToken ct)
    {
        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (payRule is null)
        {
            return ServiceResult<PayRule>.NotFound($"No pay rule with id {id}.");
        }

        if (!PayRuleRequestValidator.IsMutable(payRule))
        {
            return ServiceResult<PayRule>.Conflict(
                $"Pay rule {id} is {payRule.Status} and cannot be deleted. Only Draft rules can be " +
                "removed — an Active or Superseded rule may already be referenced by assignments or " +
                "audit snapshots.");
        }

        payRule.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(payRule);
    }
}
