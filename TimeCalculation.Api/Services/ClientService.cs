using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class ClientService(PayrollDbContext db, IClock clock)
{
    public async Task<ServiceResult<Client>> CreateAsync(CreateClientRequest request, CancellationToken ct)
    {
        var errors = ClientRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Client>.ValidationFailed(errors);
        }

        var client = new Client
        {
            Name = request.Name,
            CreatedBy = request.CreatedBy,
            CreatedDate = clock.GetCurrentInstant().ToDateTimeUtc(),
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Client>.Success(client);
    }

    public async Task<PagedResult<Client>> ListAsync(string? search, PagingQuery paging, CancellationToken ct)
    {
        var query = db.Clients.AsQueryable();
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
