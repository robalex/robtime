using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class EmployeeService(PayrollDbContext db)
{
    public async Task<ServiceResult<Employee>> CreateAsync(CreateEmployeeRequest request, CancellationToken ct)
    {
        var errors = EmployeeRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Employee>.ValidationFailed(errors);
        }

        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<Employee>.NotFound($"No client with id {request.ClientId}.");
        }

        var employee = new Employee
        {
            ClientId = request.ClientId,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName ?? string.Empty,
            LastName = request.LastName,
            Salutation = request.Salutation ?? string.Empty,
            PostNominalLetters = request.PostNominalLetters ?? string.Empty,
            MinimumWage = request.MinimumWage,
            HomeTimeZoneId = request.HomeTimeZoneId ?? "America/New_York",
            State = request.State ?? string.Empty,
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);

        return ServiceResult<Employee>.Success(employee);
    }

    public async Task<PagedResult<Employee>> ListAsync(
        int clientId, string? search, PagingQuery paging, CancellationToken ct)
    {
        var query = db.Employees.Where(e => e.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e =>
                EF.Functions.ILike(e.FirstName, $"%{search}%") || EF.Functions.ILike(e.LastName, $"%{search}%"));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((paging.NormalizedPage - 1) * paging.NormalizedPageSize)
            .Take(paging.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<Employee>
        {
            Items = items,
            TotalCount = totalCount,
            Page = paging.NormalizedPage,
            PageSize = paging.NormalizedPageSize,
        };
    }

    public async Task<ServiceResult<Employee>> GetAsync(int id, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        return employee is null
            ? ServiceResult<Employee>.NotFound($"No employee with id {id}.")
            : ServiceResult<Employee>.Success(employee);
    }

    public async Task<ServiceResult<Employee>> UpdateAsync(int id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var errors = EmployeeRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<Employee>.ValidationFailed(errors);
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
        {
            return ServiceResult<Employee>.NotFound($"No employee with id {id}.");
        }

        employee.FirstName = request.FirstName;
        employee.MiddleName = request.MiddleName ?? string.Empty;
        employee.LastName = request.LastName;
        employee.Salutation = request.Salutation ?? string.Empty;
        employee.PostNominalLetters = request.PostNominalLetters ?? string.Empty;
        employee.MinimumWage = request.MinimumWage;
        employee.HomeTimeZoneId = request.HomeTimeZoneId ?? "America/New_York";
        employee.State = request.State ?? string.Empty;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Employee>.Success(employee);
    }

    public async Task<ServiceResult<Employee>> DeleteAsync(int id, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
        {
            return ServiceResult<Employee>.NotFound($"No employee with id {id}.");
        }

        employee.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        return ServiceResult<Employee>.Success(employee);
    }
}
