using NodaTime;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Api.Contracts;

public record CreateClientPremiumPolicyRequest
{
    public required int ClientId { get; init; }
    public required string PremiumCode { get; init; }
    public required WaiverPolicy WaiverPolicy { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public string? Justification { get; init; }
}

/// <summary>
/// No ClientId (route-identified, same as UpdateDifferentialRuleRequest) and no SetBy/SetAt — those
/// are sourced from the authenticated principal and the clock, not client-supplied (CLAUDE.md /
/// task #36's "no CreatedBy in request contracts" precedent). Submitting an update re-attests the
/// determination, so the service refreshes SetBy/SetAt to whoever submitted it.
/// </summary>
public record UpdateClientPremiumPolicyRequest
{
    public required string PremiumCode { get; init; }
    public required WaiverPolicy WaiverPolicy { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public string? Justification { get; init; }
}

public sealed record ClientPremiumPolicyResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string PremiumCode { get; init; }
    public required WaiverPolicy WaiverPolicy { get; init; }
    public required string SetBy { get; init; }
    public required Instant SetAt { get; init; }
    public required LocalDate EffectiveFrom { get; init; }
    public LocalDate? EffectiveTo { get; init; }
    public string? Justification { get; init; }

    public static ClientPremiumPolicyResponse FromEntity(ClientPremiumPolicy policy) => new()
    {
        Id = policy.Id,
        ClientId = policy.ClientId,
        PremiumCode = policy.PremiumCode,
        WaiverPolicy = policy.WaiverPolicy,
        SetBy = policy.SetBy,
        SetAt = policy.SetAt,
        EffectiveFrom = policy.EffectiveFrom,
        EffectiveTo = policy.EffectiveTo,
        Justification = policy.Justification,
    };
}
