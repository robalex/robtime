namespace TimeCalculation.Persistence;

/// <summary>
/// Supplies the current request's tenant id to <see cref="PayrollDbContext"/> without pulling an
/// ASP.NET Core dependency into this project (which depends only on TimeCalculation.Model — see the
/// context's own class doc comment). The API project provides the real implementation (reads the
/// `client_id` claim off the validated Cognito JWT); tests and DevSeeder provide fixed-value ones.
/// </summary>
public interface ITenantContextAccessor
{
    int? ClientId { get; }
}

/// <summary>Fixed-value accessor for contexts constructed outside an HTTP request — DevSeeder,
/// design-time `dotnet ef` tooling, and system/background work with no principal to read claims
/// from. Not a "no tenant" bypass: a null ClientId here still filters every tenant-scoped query down
/// to nothing once the escape hatch is removed from the filter predicates (see UI_PLAN.md §5); that
/// bypass is `IgnoreQueryFilters()` at the specific call site, not a context-construction option.</summary>
public sealed class FixedTenantContextAccessor(int? clientId) : ITenantContextAccessor
{
    public int? ClientId { get; } = clientId;
}
