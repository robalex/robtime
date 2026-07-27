using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class HolidayCalendarRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreateHolidayCalendarRequest request) =>
        Validate(request.Name);

    public static IDictionary<string, string[]> Validate(UpdateHolidayCalendarRequest request) =>
        Validate(request.Name);

    private static IDictionary<string, string[]> Validate(string name)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        return errors;
    }
}
