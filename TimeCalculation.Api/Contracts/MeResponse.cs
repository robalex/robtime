using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Who the caller is, as the frontend needs it: identity and authorization facts from the validated
/// token, plus the local profile row when one exists. This is what <c>lib/permissions.ts</c> keys
/// off — never cookie/token presence (UI_PLAN.md §5).
/// </summary>
public record MeResponse
{
    public required string CognitoSub { get; init; }
    public string? Email { get; init; }

    /// <summary>Null for SystemAdmin, which scopes into one client at a time rather than owning one.</summary>
    public int? ClientId { get; init; }

    /// <summary>Null only for a Cognito user with no <c>custom:role</c> — see <see cref="IsProvisioned"/>.</summary>
    public AppRole? Role { get; init; }

    public int? EmployeeId { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>
    /// False when the caller authenticated against Cognito but has no <c>AppUser</c> row — which
    /// happens for a user created directly in the Cognito console rather than through
    /// <c>POST /users</c>, the bootstrap admin being the expected case. The frontend should treat
    /// this as "signed in but not set up" rather than silently rendering an app with no tenant.
    /// </summary>
    public required bool IsProvisioned { get; init; }
}
