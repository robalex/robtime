using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class PayRuleService(PayrollDbContext db)
{
    public async Task<ServiceResult<PayRule>> CreateAsync(CreatePayRuleRequest request, CancellationToken ct)
    {
        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists)
        {
            return ServiceResult<PayRule>.NotFound($"No client with id {request.ClientId}.");
        }

        var payRule = PayRuleRequestMapper.BuildFromRequest(request);

        var errors = PayRuleRequestValidator.ValidateConsistency(payRule);
        if (errors.Count > 0)
        {
            return ServiceResult<PayRule>.ValidationFailed(errors);
        }

        db.PayRules.Add(payRule);
        await db.SaveChangesAsync(ct);

        return ServiceResult<PayRule>.Success(payRule);
    }
}
