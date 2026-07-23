using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PositionService(PayrollDbContext db)
{
    public async Task<ServiceResult<Position>> CreateAsync(CreatePositionRequest request, CancellationToken ct)
    {
        var errors = PositionRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Position>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<Position>.NotFound($"No client with id {request.ClientId}.");
        }

        var position = new Position
        {
            ClientId = request.ClientId,
            Code = request.Code,
            Name = request.Name,
            BaseRate = request.BaseRate,
        };

        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Position>.Success(position);
    }

    public async Task<PagedResult<Position>> ListAsync(
        int clientId, string? search, PagingQuery paging, CancellationToken ct)
    {
        var query = db.Positions.Where(p => p.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{search}%") || EF.Functions.ILike(p.Code, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<Position>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<Position>> GetAsync(int id, CancellationToken ct)
    {
        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
        return position is null
            ? ServiceResult<Position>.NotFound($"No position with id {id}.")
            : ServiceResult<Position>.Success(position);
    }

    public async Task<ServiceResult<Position>> UpdateAsync(int id, UpdatePositionRequest request, CancellationToken ct)
    {
        var errors = PositionRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Position>.ValidationFailed(errors);
        }

        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (position is null)
        {
            return ServiceResult<Position>.NotFound($"No position with id {id}.");
        }

        position.Code = request.Code;
        position.Name = request.Name;
        position.BaseRate = request.BaseRate;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Position>.Success(position);
    }

    public async Task<ServiceResult<Position>> DeleteAsync(int id, CancellationToken ct)
    {
        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (position is null)
        {
            return ServiceResult<Position>.NotFound($"No position with id {id}.");
        }

        position.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Position>.Success(position);
    }
}
