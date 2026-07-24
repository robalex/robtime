using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this WebApplication app)
    {
        // Authenticated but deliberately NOT role-gated: this is how a caller discovers what role it
        // has, so requiring a role to call it would be circular — and it must stay callable by a user
        // whose custom:role claim is missing entirely, which is exactly the case the response's
        // IsProvisioned flag exists to report.
        app.MapGet("/me", GetMe).WithName("GetMe").RequireAuthorization();
    }

    private static async Task<Ok<MeResponse>> GetMe(
        ClaimsPrincipal user, CurrentUserService service, CancellationToken ct)
    {
        var me = await service.GetAsync(CallerIdentity.FromPrincipal(user), ct);
        return TypedResults.Ok(me);
    }
}
