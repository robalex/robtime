using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class ClientRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateClientRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        return errors;
    }
}
