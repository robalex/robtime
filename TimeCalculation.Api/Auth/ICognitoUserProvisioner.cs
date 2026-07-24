using TimeCalculation.Model;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// The Cognito half of the two-system write UI_PLAN.md §5 calls out for user provisioning —
/// UserProvisioningService owns the ordering (Cognito first, then the local AppUser row) and the
/// compensation on failure; this interface only wraps the actual Cognito calls, so tests can swap in
/// a fake (there's no Testcontainers-equivalent for a real Cognito pool, same reasoning as
/// TestAuthHandler standing in for real JWT validation).
/// </summary>
public interface ICognitoUserProvisioner
{
    /// <summary>Creates the Cognito user and returns its `sub` — the value AppUser.CognitoSub is
    /// keyed by. ClientId/Role are set as the custom:client_id/custom:role attributes that end up in
    /// the user's token claims (UI_PLAN.md §5).</summary>
    Task<string> CreateUserAsync(string email, int? clientId, AppRole role, CancellationToken ct);

    /// <summary>Compensating action when the local AppUser write fails after Cognito succeeded.
    /// Takes the Cognito username (this codebase creates users with Username == email, not the sub —
    /// see CognitoUserProvisioner), not the sub.</summary>
    Task DeleteUserAsync(string username, CancellationToken ct);
}
