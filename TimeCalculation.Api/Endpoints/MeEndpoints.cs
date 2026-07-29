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

        // Any authenticated role, and inherently self-scoped — there is no employee id to supply,
        // so there's nothing for a caller to point at someone else (Phase 6.4). A Supervisor+ who
        // happens to have a linked Employee row gets their own status here too; to see somebody
        // else's they use the per-employee routes, which is the whole first/third-person split
        // UI_PLAN.md's Phase 6 notes describe.
        app.MapGet("/me/clock-status", GetMyClockStatus).WithName("GetMyClockStatus")
            .RequireAuthorization(AuthorizationPolicies.Employee);
    }

    private static async Task<Ok<MeResponse>> GetMe(
        ClaimsPrincipal user, CurrentUserService service, CancellationToken ct)
    {
        var me = await service.GetAsync(CallerIdentity.FromPrincipal(user), ct);
        return TypedResults.Ok(me);
    }

    private static async Task<Results<Ok<ClockStatusResponse>, ProblemHttpResult>> GetMyClockStatus(
        ClaimsPrincipal user, EmployeeScopeResolver scopeResolver, PunchService service, CancellationToken ct)
    {
        var caller = CallerIdentity.FromPrincipal(user);
        var employeeId = await scopeResolver.ResolveOwnAsync(caller, ct);
        if (employeeId is null)
        {
            // 404, not 403: the caller is perfectly entitled to ask about their own clock — there
            // just isn't an employee record to have one. The frontend gates on /me's EmployeeId
            // before calling this at all, so this is the honest answer for a direct API caller
            // rather than a state the UI walks into.
            return TypedResults.Problem(
                detail: "No employee record is linked to this account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var status = await service.GetClockStatusAsync(employeeId.Value, ct);
        return TypedResults.Ok(status);
    }
}
