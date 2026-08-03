using TimeCalculation.Api.Contracts;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Validation;

public static class PayrollExportProfileRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePayrollExportProfileRequest request) =>
        Validate(request.Name, request.RoundingPolicy, request.AdjustmentEarningCode, request.AmountScale, request.HoursScale);

    public static IDictionary<string, string[]> Validate(UpdatePayrollExportProfileRequest request) =>
        Validate(request.Name, request.RoundingPolicy, request.AdjustmentEarningCode, request.AmountScale, request.HoursScale);

    private static IDictionary<string, string[]> Validate(
        string name, PayrollExportRoundingPolicy? roundingPolicy, string? adjustmentEarningCode,
        int? amountScale, int? hoursScale)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        if (amountScale is < 0 or > 4)
        {
            errors["amountScale"] = ["Must be between 0 and 4 decimal places."];
        }

        if (hoursScale is < 0 or > 4)
        {
            errors["hoursScale"] = ["Must be between 0 and 4 decimal places."];
        }

        if (roundingPolicy == PayrollExportRoundingPolicy.AdjustmentRow
            && string.IsNullOrWhiteSpace(adjustmentEarningCode))
        {
            errors["adjustmentEarningCode"] =
                ["Required when the rounding policy is AdjustmentRow — this is the code the residual is posted to."];
        }

        return errors;
    }
}
