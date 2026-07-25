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
    // ever gets a _tenantClientId in the first place, so it can't itself be tenant-filtered. Shares
    // VisibleTo with the by-id methods rather than hand-rolling the same IgnoreQueryFilters call, so
    // "which clients can this caller see" has exactly one answer in this file.
    public async Task<PagedResult<Client>> ListAsync(
        string? search, PagingQuery paging, AppRole? callerRole, CancellationToken ct)
    {
        var query = VisibleTo(callerRole);
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

    /// <summary>
    /// Which clients this caller may act on — the single place that decision is made, so the three
    /// methods below can't drift apart.
    ///
    /// The Client tenant filter is <c>c.Id == _tenantClientId</c>, which is right for a ClientAdmin
    /// (they see exactly their own client) but matches *nothing* for a SystemAdmin, who carries no
    /// <c>custom:client_id</c> claim by design (UI_PLAN.md §5 — SystemAdmin scopes into a client
    /// rather than owning one). Without this, every Get/Update/Delete 404s for the one role whose
    /// entire job is managing clients.
    ///
    /// IgnoreQueryFilters drops the soft-delete filter along with the tenant one, so <c>!IsDeleted</c>
    /// is re-applied explicitly — dropping it would resurrect deleted clients for SystemAdmins only,
    /// which is exactly the kind of asymmetry that hides for months.
    /// </summary>
    private IQueryable<Client> VisibleTo(AppRole? callerRole) =>
        callerRole == AppRole.SystemAdmin
            ? db.Clients.IgnoreQueryFilters().Where(c => !c.IsDeleted)
            : db.Clients;

    public async Task<ServiceResult<Client>> GetAsync(int id, AppRole? callerRole, CancellationToken ct)
    {
        var client = await VisibleTo(callerRole).FirstOrDefaultAsync(c => c.Id == id, ct);
        return client is null
            ? ServiceResult<Client>.NotFound($"No client with id {id}.")
            : ServiceResult<Client>.Success(client);
    }

    public async Task<ServiceResult<Client>> UpdateAsync(
        int id, UpdateClientRequest request, AppRole? callerRole, CancellationToken ct)
    {
        var errors = ClientRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Client>.ValidationFailed(errors);
        }

        var client = await VisibleTo(callerRole).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return ServiceResult<Client>.NotFound($"No client with id {id}.");
        }

        client.Name = request.Name;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }

    public async Task<ServiceResult<Client>> DeleteAsync(int id, AppRole? callerRole, CancellationToken ct)
    {
        var client = await VisibleTo(callerRole).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null)
        {
            return ServiceResult<Client>.NotFound($"No client with id {id}.");
        }

        client.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }
}
