using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class PunchRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePunchRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Kind == PunchKind.FixedDollar && request.Amount is null)
        {
            errors["amount"] = ["Amount is required for FixedDollar punches."];
        }

        if (request.Kind == PunchKind.FixedHours && request.Hours is null)
        {
            errors["hours"] = ["Hours is required for FixedHours punches."];
        }

        return errors;
    }
}
