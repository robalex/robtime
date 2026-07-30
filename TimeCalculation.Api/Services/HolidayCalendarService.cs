using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class HolidayCalendarService(PayrollDbContext db)
{
    public async Task<ServiceResult<HolidayCalendar>> CreateAsync(CreateHolidayCalendarRequest request, CancellationToken ct)
    {
        var errors = HolidayCalendarRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<HolidayCalendar>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<HolidayCalendar>.NotFound($"No client with id {request.ClientId}.");
        }

        var calendar = new HolidayCalendar
        {
            ClientId = request.ClientId,
            Name = request.Name,
            Dates = request.Dates ?? [],
        };

        db.HolidayCalendars.Add(calendar);
        await db.SaveChangesAsync(ct);

        return ServiceResult<HolidayCalendar>.Success(calendar);
    }

    public async Task<PagedResult<HolidayCalendar>> ListAsync(int clientId, PagingQuery paging, CancellationToken ct)
    {
        var query = db.HolidayCalendars.Where(h => h.ClientId == clientId);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(h => h.Name)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<HolidayCalendar>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<HolidayCalendar>> GetAsync(int id, CancellationToken ct)
    {
        var calendar = await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == id, ct);
        return calendar is null
            ? ServiceResult<HolidayCalendar>.NotFound($"No holiday calendar with id {id}.")
            : ServiceResult<HolidayCalendar>.Success(calendar);
    }

    public async Task<ServiceResult<HolidayCalendar>> UpdateAsync(int id, UpdateHolidayCalendarRequest request, CancellationToken ct)
    {
        var errors = HolidayCalendarRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<HolidayCalendar>.ValidationFailed(errors);
        }

        var calendar = await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (calendar is null)
        {
            return ServiceResult<HolidayCalendar>.NotFound($"No holiday calendar with id {id}.");
        }

        calendar.Name = request.Name;
        calendar.Dates = request.Dates ?? [];
        await db.SaveChangesAsync(ct);

        return ServiceResult<HolidayCalendar>.Success(calendar);
    }

    public async Task<ServiceResult<HolidayCalendar>> DeleteAsync(int id, CancellationToken ct)
    {
        var calendar = await db.HolidayCalendars.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (calendar is null)
        {
            return ServiceResult<HolidayCalendar>.NotFound($"No holiday calendar with id {id}.");
        }

        // Same reference-guard pattern as DifferentialRuleService.DeleteAsync — HolidayCalendarId is
        // a real FK column (unlike ActiveDifferentialCodes' comma-delimited set), so this pushes
        // straight into SQL rather than pulling every pay rule client-side.
        var referencingPayRules = await db.PayRules.Where(r => r.HolidayCalendarId == id).ToListAsync(ct);
        if (referencingPayRules.Count > 0)
        {
            var names = string.Join(", ", referencingPayRules.Select(r => $"'{r.Name}' ({r.Status}, v{r.Version})"));
            return ServiceResult<HolidayCalendar>.Conflict(
                $"Holiday calendar '{calendar.Name}' is still referenced by {referencingPayRules.Count} pay " +
                $"rule(s): {names}. Remove it from those pay rules before deleting.");
        }

        calendar.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<HolidayCalendar>.Success(calendar);
    }
}
