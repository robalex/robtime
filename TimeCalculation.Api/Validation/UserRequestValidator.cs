using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class UserRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors["displayName"] = ["Display name is required."];
        }

        if (request.ClientId is null && request.Role != AppRole.SystemAdmin)
        {
            errors["clientId"] = ["Client id is required for every role except SystemAdmin."];
        }

        return errors;
    }
}
