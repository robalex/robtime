using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Auth;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Decides which employee a caller is allowed to act on — the self-service scoping mechanism
/// UI_PLAN.md's Phase 6 notes describe, built once here and reused by every route that opens to the
/// Employee role (6.4's clock, then 6.5/6.6 as they need it).
///
/// The rule: <b>Supervisor and above</b> act on any employee in their tenant (the tenant query
/// filters already stop them reaching outside it), exactly as they could before self-service
/// existed. <b>Employee</b> is pinned to their own <c>AppUser.EmployeeId</c> and nothing else.
///
/// The employee id for an Employee caller is always read from the server-side <c>AppUser</c> row,
/// never taken from the request — a self-service route that trusts a client-supplied employee id is
/// strictly worse than the Supervisor-only route it replaced, since it would let any authenticated
/// employee punch as a colleague. A mismatched id is rejected rather than silently overridden, so a
/// buggy client fails loudly instead of writing to the wrong employee.
/// </summary>
public class EmployeeScopeResolver(PayrollDbContext db)
{
    public async Task<ServiceResult<int>> ResolveAsync(
        CallerIdentity caller, int? requestedEmployeeId, CancellationToken ct)
    {
        if (caller.Role is not AppRole.Employee)
        {
            return requestedEmployeeId is { } supervisorTarget
                ? ServiceResult<int>.Success(supervisorTarget)
                : ServiceResult<int>.ValidationFailed(new Dictionary<string, string[]>
                {
                    ["employeeId"] = ["EmployeeId is required."],
                });
        }

        var ownEmployeeId = await ResolveOwnAsync(caller, ct);
        if (ownEmployeeId is null)
        {
            return ServiceResult<int>.Forbidden(
                "This account has no employee record linked to it, so it cannot record time.");
        }

        if (requestedEmployeeId is { } requested && requested != ownEmployeeId)
        {
            return ServiceResult<int>.Forbidden("You can only act on your own employee record.");
        }

        return ServiceResult<int>.Success(ownEmployeeId.Value);
    }

    /// <summary>
    /// The caller's own linked employee id, or null when their account has none (every
    /// ClientAdmin/Supervisor account created without one, plus any user provisioned before the
    /// employee row existed). IgnoreQueryFilters for the same reason CurrentUserService does it:
    /// this is a primary-key lookup of your own row, and the tenant filter on AppUser.ClientId is
    /// circular here — outright wrong for a SystemAdmin, whose row carries a null ClientId by design.
    /// </summary>
    public async Task<int?> ResolveOwnAsync(CallerIdentity caller, CancellationToken ct)
    {
        var appUser = await db.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.CognitoSub == caller.CognitoSub, ct);
        return appUser?.EmployeeId;
    }
}
