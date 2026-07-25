using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// Resolves the tenant for the current request. Registered Scoped, matching PayrollDbContext's own
/// lifetime — one resolution per request, not per query.
///
/// Two sources, and which one applies is decided by role, never by what the caller sent:
/// <list type="bullet">
/// <item><b>SystemAdmin</b> — the <see cref="TenantSelection.HeaderName"/> request header. A
/// SystemAdmin owns no client (it scopes *into* one, UI_PLAN.md §5), so it has no
/// <c>custom:client_id</c> claim to read and the selection has to travel per request.</item>
/// <item><b>Every other role</b> — the <c>custom:client_id</c> claim, always. The header is ignored
/// outright: not merged, not consulted, and explicitly not a fallback when the claim is missing.
/// That rule is the entire security boundary here — relax it and any authenticated user can read
/// another tenant's data by setting one header. <c>TenantIsolationTests</c> pins it.</item>
/// </list>
///
/// Returns null outside an HTTP request (e.g. `dotnet run -- --seed`), for a SystemAdmin who hasn't
/// selected a client, and for a token carrying no client claim. Null means "no tenant", which the
/// query filters translate to *no rows* rather than all rows — fail closed.
/// </summary>
public sealed class HttpContextTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
{
    public int? ClientId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            var roleClaim = context.User.FindFirst(TenantClaimTypes.Role)?.Value;
            if (roleClaim == nameof(AppRole.SystemAdmin))
            {
                var header = context.Request.Headers[TenantSelection.HeaderName].ToString();
                return int.TryParse(header, out var selected) ? selected : null;
            }

            var claim = context.User.FindFirst(TenantClaimTypes.ClientId)?.Value;
            return int.TryParse(claim, out var clientId) ? clientId : null;
        }
    }
}
