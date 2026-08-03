using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Pure request-shape validation — no DB access. Every rule here is derived from exactly what
/// PaySummarizer sets on a real PayLineItem, so a mapping that passes can always match a line, and
/// one that would mispay (an Hours mapping on a rate-derived line type) is rejected before it's ever
/// saved rather than silently underpaying someone once export runs.
/// </summary>
public static class PayrollEarningCodeMappingRequestValidator
{
    public static IDictionary<string, string[]> Validate(CreatePayrollEarningCodeMappingRequest request) =>
        Validate(request.LineType, request.LineCode, request.EarningCode, request.ValueBasis);

    public static IDictionary<string, string[]> Validate(UpdatePayrollEarningCodeMappingRequest request) =>
        Validate(request.LineType, request.LineCode, request.EarningCode, request.ValueBasis);

    private static IDictionary<string, string[]> Validate(
        PayLineType lineType, string lineCode, string earningCode, PayrollExportValueBasis valueBasis)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(earningCode))
        {
            errors["earningCode"] = ["Earning code is required."];
        }

        var lineCodeError = DescribeLineCodeError(lineType, lineCode);
        if (lineCodeError is not null)
        {
            errors["lineCode"] = [lineCodeError];
        }

        if (RequiresAmountBasis(lineType) && valueBasis != PayrollExportValueBasis.Amount)
        {
            errors["valueBasis"] = [DescribeWhyAmountRequired(lineType)];
        }

        return errors;
    }

    /// <summary>Mirrors PaySummarizer's Code assignment for each Type exactly — a mapping shaped any
    /// other way could never match a real line, which is a silently dead configuration rather than
    /// an outright error at the point it's created.</summary>
    private static string? DescribeLineCodeError(PayLineType lineType, string lineCode) => lineType switch
    {
        PayLineType.Regular or PayLineType.FixedHours => lineCode.Length == 0
            ? null
            : $"{lineType} lines always carry an empty code; '{lineCode}' can never match one.",
        PayLineType.OvertimePremium => lineCode is "OVERTIME" or "DOUBLETIME"
            ? null
            : "Overtime premium lines are coded 'OVERTIME' or 'DOUBLETIME'.",
        PayLineType.Bonus => Enum.TryParse<BonusKind>(lineCode, out _)
            ? null
            : $"Bonus lines are coded by kind: '{nameof(BonusKind.Discretionary)}' or " +
              $"'{nameof(BonusKind.NonDiscretionary)}'.",
        PayLineType.Differential or PayLineType.Premium => string.IsNullOrWhiteSpace(lineCode)
            ? $"{lineType} mappings need the specific rule code they apply to."
            : null,
        _ => $"'{lineType}' is not a recognized pay line type.",
    };

    /// <summary>OvertimePremium and Premium are priced off the weighted FLSA regular rate no payroll
    /// system can recompute from raw hours; Bonus lines always carry Hours == 0, so an Hours mapping
    /// would export a zero and pay nothing. All three must be Amount-basis, not merely default to it.</summary>
    private static bool RequiresAmountBasis(PayLineType lineType) =>
        lineType is PayLineType.OvertimePremium or PayLineType.Premium or PayLineType.Bonus;

    private static string DescribeWhyAmountRequired(PayLineType lineType) => lineType switch
    {
        PayLineType.OvertimePremium or PayLineType.Premium =>
            $"{lineType} is priced from the weighted regular rate, which the payroll provider cannot " +
            "recompute from raw hours — map it as Amount, not Hours.",
        PayLineType.Bonus =>
            "Bonus lines always carry zero hours; an Hours mapping would export nothing. Map it as Amount.",
        _ => $"{lineType} must be mapped as Amount.",
    };
}
