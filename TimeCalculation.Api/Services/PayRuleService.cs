using Microsoft.EntityFrameworkCore;
using NodaTime;
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

    /// <summary>
    /// Promotes a Draft to Active as of <paramref name="request"/>'s EffectiveFrom (Gap F). If the
    /// rule's family already has an Active version, that version is Superseded first — its
    /// EffectiveTo is set to the day before the new version's EffectiveFrom, so the two versions'
    /// windows are adjacent and never overlap. Both PayRule.EffectiveFrom/EffectiveTo are pure
    /// version-history bookkeeping (see PayRule's own doc comment) — the calculation pipeline never
    /// reads them; it resolves the applicable rule purely through PayRuleAssignment's date range.
    /// </summary>
    public async Task<ServiceResult<PayRule>> ActivateAsync(int id, ActivatePayRuleRequest request, CancellationToken ct)
    {
        var payRule = await db.PayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (payRule is null)
        {
            return ServiceResult<PayRule>.NotFound($"No pay rule with id {id}.");
        }

        if (!PayRuleRequestValidator.CanActivate(payRule))
        {
            return ServiceResult<PayRule>.Conflict(
                $"Pay rule {id} is {payRule.Status} and can't be activated — only a Draft can be promoted.");
        }

        var currentActive = await db.PayRules.FirstOrDefaultAsync(
            r => r.RuleFamilyId == payRule.RuleFamilyId && r.Status == PayRuleStatus.Active, ct);
        if (currentActive is not null)
        {
            if (request.EffectiveFrom <= currentActive.EffectiveFrom)
            {
                return ServiceResult<PayRule>.ValidationFailed(new Dictionary<string, string[]>
                {
                    ["effectiveFrom"] =
                    [
                        $"Must be after the currently active version's effective date ({currentActive.EffectiveFrom:yyyy-MM-dd}).",
                    ],
                });
            }

            currentActive.Status = PayRuleStatus.Superseded;
            currentActive.EffectiveTo = request.EffectiveFrom.PlusDays(-1);
        }

        payRule.Status = PayRuleStatus.Active;
        payRule.EffectiveFrom = request.EffectiveFrom;
        payRule.EffectiveTo = null;

        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(payRule);
    }

    /// <summary>
    /// Forks a new Draft from an Active/Superseded rule — the "create a new version instead of
    /// editing" workflow Gap F's design calls for. The new row's Version is the family's current
    /// max + 1 (not source.Version + 1), so forking from an old Superseded row after newer versions
    /// already exist can't collide with a version number that's already taken.
    /// </summary>
    public async Task<ServiceResult<PayRule>> CreateNewVersionAsync(int id, CancellationToken ct)
    {
        var source = await db.PayRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (source is null)
        {
            return ServiceResult<PayRule>.NotFound($"No pay rule with id {id}.");
        }

        if (!PayRuleRequestValidator.CanForkNewVersion(source))
        {
            return ServiceResult<PayRule>.Conflict(
                $"Pay rule {id} is already a Draft — edit it directly instead of forking a new version.");
        }

        var maxVersion = await db.PayRules
            .Where(r => r.RuleFamilyId == source.RuleFamilyId)
            .MaxAsync(r => r.Version, ct);

        var newVersion = new PayRule
        {
            ClientId = source.ClientId,
            Name = source.Name,
            Description = source.Description,
            TemplateCode = source.TemplateCode,
            TemplateVersion = source.TemplateVersion,
            RuleFamilyId = source.RuleFamilyId,
            Version = maxVersion + 1,
            Status = PayRuleStatus.Draft,
            PunchPairResetHours = source.PunchPairResetHours,
            MaxShiftLengthHours = source.MaxShiftLengthHours,
            DistanceBetweenShiftsHours = source.DistanceBetweenShiftsHours,
            ExpectedBreakLengthMinutes = source.ExpectedBreakLengthMinutes,
            ExpectedLunchLengthMinutes = source.ExpectedLunchLengthMinutes,
            RoundingRule = new RoundingRule
            {
                RoundingStrategy = source.RoundingRule.RoundingStrategy,
                RoundingIntervalMinutes = source.RoundingRule.RoundingIntervalMinutes,
                RoundingGraceMinutes = source.RoundingRule.RoundingGraceMinutes,
            },
            ShiftDateStrategy = source.ShiftDateStrategy,
            ActivePremiumCodes = source.ActivePremiumCodes.ToHashSet(),
            ActiveDifferentialCodes = source.ActiveDifferentialCodes.ToHashSet(),
            WorkweekStartDay = source.WorkweekStartDay,
            OvertimeRule = new OvertimeRule
            {
                WeeklyOvertimeThresholdHours = source.OvertimeRule.WeeklyOvertimeThresholdHours,
                HasDailyOvertime = source.OvertimeRule.HasDailyOvertime,
                DailyOvertimeThresholdHours = source.OvertimeRule.DailyOvertimeThresholdHours,
                DailyDoubletimeThresholdHours = source.OvertimeRule.DailyDoubletimeThresholdHours,
                HasSeventhDayRule = source.OvertimeRule.HasSeventhDayRule,
            },
        };

        db.PayRules.Add(newVersion);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(newVersion);
    }
}
