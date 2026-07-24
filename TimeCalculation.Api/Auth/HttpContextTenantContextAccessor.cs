using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// Reads the tenant id off the current request's validated Cognito JWT. Registered Scoped, matching
/// PayrollDbContext's own lifetime — one resolution per request, not per query.
///
/// Returns null outside an HTTP request (no <see cref="IHttpContextAccessor.HttpContext"/> — e.g.
/// `dotnet run -- --seed`) or when no `client_id` claim is present (no token, or a SystemAdmin who
/// hasn't selected a client yet) — same "no tenant" meaning as <see cref="FixedTenantContextAccessor"/>,
/// just sourced from claims instead of a fixed value.
/// </summary>
public sealed class HttpContextTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
{
    public int? ClientId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(TenantClaimTypes.ClientId);
            return claim is not null && int.TryParse(claim.Value, out var clientId) ? clientId : null;
        }
    }
}
