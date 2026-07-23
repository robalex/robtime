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
}
