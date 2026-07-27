using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Shared reference data — no ClientId, no tenant filter (see PayrollDbContext's mapping). Every
/// endpoint is SystemAdmin-gated at the route level rather than here, matching how ClientService's
/// cross-tenant list/create endpoints are gated (see ClientEndpoints).
/// </summary>
public class StateMinimumWageService(PayrollDbContext db)
{
    public async Task<ServiceResult<StateMinimumWage>> CreateAsync(CreateStateMinimumWageRequest request, CancellationToken ct)
    {
        var errors = StateMinimumWageRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<StateMinimumWage>.ValidationFailed(errors);
        }

        var conflict = await FindConflictAsync(
            request.State, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: null, ct);
        if (conflict is not null)
        {
            return ServiceResult<StateMinimumWage>.Conflict(
                $"{request.State} already has a rate for this period. {StateMinimumWageRequestValidator.DescribeConflict(conflict)}");
        }

        var wage = new StateMinimumWage
        {
            State = request.State,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Amount = request.Amount,
        };

        db.StateMinimumWages.Add(wage);
        await db.SaveChangesAsync(ct);

        return ServiceResult<StateMinimumWage>.Success(wage);
    }

    public async Task<PagedResult<StateMinimumWage>> ListAsync(string? state, PagingQuery paging, CancellationToken ct)
    {
        var query = db.StateMinimumWages.AsQueryable();
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(w => w.State == state);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(w => w.State).ThenByDescending(w => w.EffectiveFrom)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<StateMinimumWage>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<StateMinimumWage>> GetAsync(int id, CancellationToken ct)
    {
        var wage = await db.StateMinimumWages.FirstOrDefaultAsync(w => w.Id == id, ct);
        return wage is null
            ? ServiceResult<StateMinimumWage>.NotFound($"No state minimum wage with id {id}.")
            : ServiceResult<StateMinimumWage>.Success(wage);
    }

    public async Task<ServiceResult<StateMinimumWage>> UpdateAsync(int id, UpdateStateMinimumWageRequest request, CancellationToken ct)
    {
        var errors = StateMinimumWageRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<StateMinimumWage>.ValidationFailed(errors);
        }

        var wage = await db.StateMinimumWages.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wage is null)
        {
            return ServiceResult<StateMinimumWage>.NotFound($"No state minimum wage with id {id}.");
        }

        var conflict = await FindConflictAsync(
            request.State, new DateRange(request.EffectiveFrom, request.EffectiveTo), excludeId: id, ct);
        if (conflict is not null)
        {
            return ServiceResult<StateMinimumWage>.Conflict(
                $"{request.State} already has a rate for this period. {StateMinimumWageRequestValidator.DescribeConflict(conflict)}");
        }

        wage.State = request.State;
        wage.EffectiveFrom = request.EffectiveFrom;
        wage.EffectiveTo = request.EffectiveTo;
        wage.Amount = request.Amount;
        await db.SaveChangesAsync(ct);

        return ServiceResult<StateMinimumWage>.Success(wage);
    }

    public async Task<ServiceResult<StateMinimumWage>> DeleteAsync(int id, CancellationToken ct)
    {
        var wage = await db.StateMinimumWages.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wage is null)
        {
            return ServiceResult<StateMinimumWage>.NotFound($"No state minimum wage with id {id}.");
        }

        // Hard delete: like PayRuleAssignment, this is a statement about a date range rather than a
        // record with its own audit history — no ClientId/IsDeleted on the model at all.
        db.StateMinimumWages.Remove(wage);
        await db.SaveChangesAsync(ct);

        return ServiceResult<StateMinimumWage>.Success(wage);
    }

    private async Task<DateRange?> FindConflictAsync(string state, DateRange proposed, int? excludeId, CancellationToken ct)
    {
        var existing = await db.StateMinimumWages
            .Where(w => w.State == state && (excludeId == null || w.Id != excludeId))
            .Select(w => new { w.EffectiveFrom, w.EffectiveTo })
            .ToListAsync(ct);

        return StateMinimumWageRequestValidator.FindConflict(
            proposed, existing.Select(w => new DateRange(w.EffectiveFrom, w.EffectiveTo)));
    }
}
