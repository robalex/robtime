using System.Security.Claims;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// The authenticated caller, projected out of the validated token's claims into plain data — so
/// services take this instead of a <see cref="ClaimsPrincipal"/> (keeping ASP.NET Core types out of
/// the service layer, per CLAUDE.md) and endpoints stop hand-parsing the same three claims.
///
/// <see cref="Role"/> and <see cref="ClientId"/> are nullable because a token can legitimately lack
/// them: a SystemAdmin carries no <c>custom:client_id</c> (it scopes into one client at a time —
/// UI_PLAN.md §5), and a Cognito user created outside the <c>POST /users</c> flow — the very first
/// bootstrap admin, for instance — may carry no <c>custom:role</c> yet. Authorization policies
/// already reject a missing role for anything that matters; this type just refuses to pretend the
/// claim was there.
/// </summary>
public sealed record CallerIdentity
{
    public required string CognitoSub { get; init; }
    public string? Email { get; init; }
    public int? ClientId { get; init; }
    public AppRole? Role { get; init; }

    public static CallerIdentity FromPrincipal(ClaimsPrincipal principal)
    {
        var clientIdClaim = principal.FindFirst(TenantClaimTypes.ClientId);
        var roleClaim = principal.FindFirst(TenantClaimTypes.Role);

        return new CallerIdentity
        {
            CognitoSub = principal.FindFirst(TenantClaimTypes.Sub)?.Value ?? string.Empty,
            Email = principal.FindFirst(TenantClaimTypes.Email)?.Value,
            ClientId = clientIdClaim is not null && int.TryParse(clientIdClaim.Value, out var clientId)
                ? clientId
                : null,
            Role = roleClaim is not null && Enum.TryParse<AppRole>(roleClaim.Value, out var role)
                ? role
                : null,
        };
    }
}
