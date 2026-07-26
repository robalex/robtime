using NodaTime;

namespace TimeCalculation.Api.Contracts;

/// <summary>Promotes a Draft pay rule to Active as of this date (Gap F's versioning workflow).</summary>
public record ActivatePayRuleRequest
{
    public required LocalDate EffectiveFrom { get; init; }
}
