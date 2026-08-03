using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

public static class PayrollExportRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePayrollExportRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.PeriodEnd < request.PeriodStart)
        {
            errors["periodEnd"] = ["The end date cannot be before the start date."];
        }

        return errors;
    }
}
