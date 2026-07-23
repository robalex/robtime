using TimeCalculation.Calculation.Premiums;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Reads from PremiumRegistry (a static, code-registered lookup in the engine) — no DB access, no
/// PayrollDbContext dependency, so this doesn't need the ServiceResult&lt;T&gt; success/failure
/// shape the other services use; there's no failure mode to report.
/// </summary>
public class PremiumMetadataService
{
    public IReadOnlyList<IPremiumRule> GetAll() => PremiumRegistry.Resolve(PremiumRegistry.AllCodes);
}
