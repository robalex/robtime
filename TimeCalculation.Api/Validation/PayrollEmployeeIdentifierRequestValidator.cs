using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

public static class PayrollEmployeeIdentifierRequestValidator
{
    private const int MaxLength = 64;

    // Cheap now, saves a slice-3 writer from having to escape these out of a delimited export file.
    private static readonly char[] DisallowedCharacters = [',', '"', '\r', '\n'];

    public static IDictionary<string, string[]> Validate(CreatePayrollEmployeeIdentifierRequest request) =>
        Validate(request.ExternalEmployeeId);

    public static IDictionary<string, string[]> Validate(UpdatePayrollEmployeeIdentifierRequest request) =>
        Validate(request.ExternalEmployeeId);

    private static IDictionary<string, string[]> Validate(string externalEmployeeId)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmed = externalEmployeeId.Trim();

        if (trimmed.Length == 0)
        {
            errors["externalEmployeeId"] = ["The provider's employee id is required."];
        }
        else if (trimmed.Length > MaxLength)
        {
            errors["externalEmployeeId"] = [$"Must be {MaxLength} characters or fewer."];
        }
        else if (trimmed.IndexOfAny(DisallowedCharacters) >= 0)
        {
            errors["externalEmployeeId"] = ["Cannot contain a comma, quote, or line break."];
        }

        return errors;
    }
}
