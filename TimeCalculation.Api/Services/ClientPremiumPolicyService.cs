using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class ClientPremiumPolicyService(PayrollDbContext db, IClock clock)
{
    public async Task<ServiceResult<ClientPremiumPolicy>> CreateAsync(
        CreateClientPremiumPolicyRequest request, string setBy, CancellationToken ct)
    {
        var errors = ClientPremiumPolicyRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ClientPremiumPolicy>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<ClientPremiumPolicy>.NotFound($"No client with id {request.ClientId}.");
        }

        var conflict = await FindConflictAsync(
            request.ClientId, request.PremiumCode,
            new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<ClientPremiumPolicy>.Conflict(
                $"A client can have only one waiver policy per premium code at a time. " +
                $"{ClientPremiumPolicyRequestValidator.DescribeConflict(conflict)}");
        }

        var policy = new ClientPremiumPolicy
        {
            ClientId = request.ClientId,
            PremiumCode = request.PremiumCode,
            WaiverPolicy = request.WaiverPolicy,
            SetBy = setBy,
            SetAt = clock.GetCurrentInstant(),
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Justification = request.Justification,
        };

        db.ClientPremiumPolicies.Add(policy);
        await db.SaveChangesAsync(ct);

        return ServiceResult<ClientPremiumPolicy>.Success(policy);
    }

    public async Task<PagedResult<ClientPremiumPolicy>> ListAsync(
        int clientId, string? premiumCode, PagingQuery paging, CancellationToken ct)
    {
        var query = db.ClientPremiumPolicies.Where(p => p.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(premiumCode))
        {
            query = query.Where(p => p.PremiumCode == premiumCode);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.PremiumCode)
            .ThenByDescending(p => p.EffectiveFrom)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<ClientPremiumPolicy>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<ClientPremiumPolicy>> GetAsync(int id, CancellationToken ct)
    {
        var policy = await db.ClientPremiumPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        return policy is null
            ? ServiceResult<ClientPremiumPolicy>.NotFound($"No client premium policy with id {id}.")
            : ServiceResult<ClientPremiumPolicy>.Success(policy);
    }

    public async Task<ServiceResult<ClientPremiumPolicy>> UpdateAsync(
        int id, UpdateClientPremiumPolicyRequest request, string setBy, CancellationToken ct)
    {
        var errors = ClientPremiumPolicyRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ClientPremiumPolicy>.ValidationFailed(errors);
        }

        var policy = await db.ClientPremiumPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return ServiceResult<ClientPremiumPolicy>.NotFound($"No client premium policy with id {id}.");
        }

        var conflict = await FindConflictAsync(
            policy.ClientId, request.PremiumCode,
            new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<ClientPremiumPolicy>.Conflict(
                $"A client can have only one waiver policy per premium code at a time. " +
                $"{ClientPremiumPolicyRequestValidator.DescribeConflict(conflict)}");
        }

        policy.PremiumCode = request.PremiumCode;
        policy.WaiverPolicy = request.WaiverPolicy;
        policy.EffectiveFrom = request.EffectiveFrom;
        policy.EffectiveTo = request.EffectiveTo;
        policy.Justification = request.Justification;
        // Submitting an update re-attests the determination — whoever submits it is now the one on
        // record for it, same as a fresh Create.
        policy.SetBy = setBy;
        policy.SetAt = clock.GetCurrentInstant();

        await db.SaveChangesAsync(ct);

        return ServiceResult<ClientPremiumPolicy>.Success(policy);
    }

    public async Task<ServiceResult<ClientPremiumPolicy>> DeleteAsync(int id, CancellationToken ct)
    {
        var policy = await db.ClientPremiumPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return ServiceResult<ClientPremiumPolicy>.NotFound($"No client premium policy with id {id}.");
        }

        policy.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<ClientPremiumPolicy>.Success(policy);
    }

    private async Task<DateRange?> FindConflictAsync(
        int clientId, string premiumCode, DateRange proposed, int? excludeId, CancellationToken ct)
    {
        // Fetching the ranges rather than expressing the overlap in SQL keeps the rule in one
        // testable place (mirrors PayRuleAssignmentService) — a client's policy history for a single
        // premium code is a handful of rows, not a scan.
        var existing = await db.ClientPremiumPolicies
            .Where(p => p.ClientId == clientId && p.PremiumCode == premiumCode
                && (excludeId == null || p.Id != excludeId))
            .Select(p => new { p.EffectiveFrom, p.EffectiveTo })
            .ToListAsync(ct);

        return ClientPremiumPolicyRequestValidator.FindConflict(
            proposed, existing.Select(p => new DateRange(p.EffectiveFrom, p.EffectiveTo)));
    }
}
