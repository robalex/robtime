namespace TimeCalculation.Api.Auth;

/// <summary>
/// How a SystemAdmin's currently-selected client travels (UI_PLAN.md §5). Deliberately a request
/// header rather than a token claim: the selection is transient session state ("which client am I
/// looking at?"), not identity ("whose client is this?"). Putting it in the token would mean an
/// AdminUpdateUserAttributes call plus a refresh on every switch, and would leave
/// <c>AppUser.ClientId</c> lying about a SystemAdmin's actual scope.
/// </summary>
public static class TenantSelection
{
    /// <summary>
    /// Honoured only for SystemAdmin — see <see cref="HttpContextTenantContextAccessor"/>. For any
    /// other role this header is inert, which is what stops it being a one-header cross-tenant read.
    /// </summary>
    public const string HeaderName = "X-RobTime-Client-Id";
}
