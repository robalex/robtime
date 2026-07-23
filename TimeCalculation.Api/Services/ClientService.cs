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
}
