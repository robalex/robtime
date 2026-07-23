using TimeCalculation.Calculation.Premiums;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Api.Contracts;

public sealed record PremiumRuleMetadataResponse
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Jurisdiction Jurisdiction { get; init; }
    public required WaiverPolicy WaiverPolicy { get; init; }

    public static PremiumRuleMetadataResponse FromRule(IPremiumRule rule) => new()
    {
        Code = rule.Code,
        Name = rule.Name,
        Description = rule.Description,
        Jurisdiction = rule.Jurisdiction,
        WaiverPolicy = rule.WaiverPolicy,
    };
}
