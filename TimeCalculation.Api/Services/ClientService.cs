using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class ClientService(PayrollDbContext db, IClock clock)
{
    public async Task<ServiceResult<Client>> CreateAsync(CreateClientRequest request, string createdBy, CancellationToken ct)
    {
        var errors = ClientRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Client>.ValidationFailed(errors);
        }

        var client = new Client
        {
            Name = request.Name,
            CreatedBy = createdBy,
            CreatedDate = clock.GetCurrentInstant().ToDateTimeUtc(),
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }

    // SystemAdmin-only (see the endpoint's RequireAuthorization policy) and the one genuinely
    // cross-tenant read in the system — listing clients to choose one is how a SystemAdmin session
    // ever gets a _tenantClientId in the first place, so it can't itself be tenant-filtered.
    // IgnoreQueryFilters is the escape hatch UI_PLAN.md §5 calls out, used here at the one call site
    // that legitimately needs it, not baked into the filter predicate itself.
    public async Task<PagedResult<Client>> ListAsync(string? search, PagingQuery paging, CancellationToken ct)
    {
        var query = db.Clients.IgnoreQueryFilters().Where(c => !c.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<Client>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<Client>> GetAsync(int id, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return client is null
            ? ServiceResult<Client>.NotFound($"No client with id {id}.")
            : ServiceResult<Client>.Success(client);
    }

    public async Task<ServiceResult<Client>> UpdateAsync(int id, UpdateClientRequest request, CancellationToken ct)
    {
        var errors = ClientRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Client>.ValidationFailed(errors);
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return ServiceResult<Client>.NotFound($"No client with id {id}.");
        }

        client.Name = request.Name;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }

    public async Task<ServiceResult<Client>> DeleteAsync(int id, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return ServiceResult<Client>.NotFound($"No client with id {id}.");
        }

        client.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }
}
