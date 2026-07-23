namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Every field but ClientId/Name is optional — anything else omitted falls back to PayRule's own
/// default (see TimeCalculation.Model.PayRules.PayRule), so this never duplicates those defaults
/// itself. No Version field — versioning (Gap F) is managed by the system, not client-supplied;
/// a new rule always starts at Version 1.
/// </summary>
public sealed record CreatePayRuleRequest : PayRuleFieldsRequest
{
    public required int ClientId { get; init; }
    public required string Name { get; init; }
}
