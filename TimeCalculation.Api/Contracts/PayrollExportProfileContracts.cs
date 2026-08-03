using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public record CreatePayrollExportProfileRequest
{
    public required int ClientId { get; init; }
    public required string Name { get; init; }
    public required PayrollProvider Provider { get; init; }
    public PayrollExportGrouping? Grouping { get; init; }
    public PayrollExportRoundingPolicy? RoundingPolicy { get; init; }
    public string? AdjustmentEarningCode { get; init; }
    public int? AmountScale { get; init; }
    public int? HoursScale { get; init; }
}

/// <summary>No ClientId (route-identified via the profile itself, same as UpdateDifferentialRuleRequest).</summary>
public record UpdatePayrollExportProfileRequest
{
    public required string Name { get; init; }
    public required PayrollProvider Provider { get; init; }
    public PayrollExportGrouping? Grouping { get; init; }
    public PayrollExportRoundingPolicy? RoundingPolicy { get; init; }
    public string? AdjustmentEarningCode { get; init; }
    public int? AmountScale { get; init; }
    public int? HoursScale { get; init; }
}

public sealed record PayrollExportProfileResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string Name { get; init; }
    public required PayrollProvider Provider { get; init; }
    public required PayrollExportGrouping Grouping { get; init; }
    public required PayrollExportRoundingPolicy RoundingPolicy { get; init; }
    public required string AdjustmentEarningCode { get; init; }
    public required int AmountScale { get; init; }
    public required int HoursScale { get; init; }

    public static PayrollExportProfileResponse FromEntity(PayrollExportProfile profile) => new()
    {
        Id = profile.Id,
        ClientId = profile.ClientId,
        Name = profile.Name,
        Provider = profile.Provider,
        Grouping = profile.Grouping,
        RoundingPolicy = profile.RoundingPolicy,
        AdjustmentEarningCode = profile.AdjustmentEarningCode,
        AmountScale = profile.AmountScale,
        HoursScale = profile.HoursScale,
    };
}
