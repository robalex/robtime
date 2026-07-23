namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Every field is optional — anything omitted leaves the existing PayRule's current value alone
/// (unlike Create, "omitted" here means "don't touch it," not "fall back to PayRule's compile-time
/// default"). No ClientId — which client owns a rule isn't something a caller changes via update.
/// Only accepted while the rule's Status is Draft (Gap F) — see PayRuleRequestValidator.
/// </summary>
public sealed record UpdatePayRuleRequest : PayRuleFieldsRequest
{
    public string? Name { get; init; }
}
