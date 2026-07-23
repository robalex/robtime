using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class EmployeeRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateEmployeeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors["firstName"] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors["lastName"] = ["Last name is required."];
        }

        if (request.MinimumWage < 0)
        {
            errors["minimumWage"] = ["Minimum wage cannot be negative."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> Validate(UpdateEmployeeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors["firstName"] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors["lastName"] = ["Last name is required."];
        }

        if (request.MinimumWage < 0)
        {
            errors["minimumWage"] = ["Minimum wage cannot be negative."];
        }

        return errors;
    }
}
