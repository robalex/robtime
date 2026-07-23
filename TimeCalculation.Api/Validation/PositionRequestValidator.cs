using TimeCalculation.Api.Contracts;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own.</summary>
public static class PositionRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePositionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors["code"] = ["Code is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (request.BaseRate < 0)
        {
            errors["baseRate"] = ["Base rate cannot be negative."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> Validate(UpdatePositionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors["code"] = ["Code is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (request.BaseRate < 0)
        {
            errors["baseRate"] = ["Base rate cannot be negative."];
        }

        return errors;
    }
}
