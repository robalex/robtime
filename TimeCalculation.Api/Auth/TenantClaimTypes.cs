namespace TimeCalculation.Api.Auth;

/// <summary>
/// Claim names carried on the Cognito-issued JWT. `client_id`/`role` are Cognito custom attributes
/// (`custom:client_id`, `custom:role`) mapped into token claims at provisioning time — see
/// UI_PLAN.md §5's "claims carry client_id/role, not a DB round trip per request." `MapInboundClaims`
/// is disabled where the token is validated (see Program.cs) so these names arrive unchanged instead
/// of being remapped to the long-form XML schema URIs ASP.NET Core uses by default.
/// </summary>
public static class TenantClaimTypes
{
    public const string ClientId = "custom:client_id";
    public const string Role = "custom:role";
    public const string Sub = "sub";

    /// <summary>Standard OIDC claim, present because email is the pool's sign-in identifier.</summary>
    public const string Email = "email";
}
