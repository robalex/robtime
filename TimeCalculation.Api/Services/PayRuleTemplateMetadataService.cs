using TimeCalculation.Api;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Reads from PayRuleTemplateRegistry (a static, code-registered lookup) — no DB access, mirrors
/// PremiumMetadataService for the same reason.
/// </summary>
public class PayRuleTemplateMetadataService
{
    public IReadOnlyList<PayRuleTemplate> GetAll() => PayRuleTemplateRegistry.All;
}
