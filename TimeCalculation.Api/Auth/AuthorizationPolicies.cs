using Microsoft.AspNetCore.Authorization;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// Role policies keyed off the `custom:role` claim — never off "is there a cookie"/"is there a
/// bearer token" (UI_PLAN.md §5). Each policy is "this role or higher," mirroring the informal
/// hierarchy in the role table (SystemAdmin can do anything ClientAdmin can, etc.) rather than an
/// exact-match role check, since SystemAdmin explicitly acts as ClientAdmin within its selected
/// client (§5's "SystemAdmin scoping" decision) instead of needing a separate code path.
///
/// Applied per the role table (UI_PLAN.md §5): mutations on client configuration (employees,
/// positions, pay rules, differentials, holiday calendars, waiver policies, assignments) require
/// ClientAdmin — "everything within one client." Reads stay open to any authenticated role,
/// matching Employee's "read-only on everything else." Punch creation requires Supervisor for now
/// ("view/edit punches for their client") — self-service Employee punch entry needs the
/// PunchChangeRequest/per-employee scoping Phase 6 hasn't built yet, so Employee can't hit it via
/// this route until that lands.
/// </summary>
public static class AuthorizationPolicies
{
    public const string SystemAdmin = "SystemAdmin";
    public const string ClientAdmin = "ClientAdmin";
    public const string Supervisor = "Supervisor";
    public const string Employee = "Employee";

    private static readonly AppRole[] SystemAdminOrHigher = [AppRole.SystemAdmin];
    private static readonly AppRole[] ClientAdminOrHigher = [AppRole.SystemAdmin, AppRole.ClientAdmin];
    private static readonly AppRole[] SupervisorOrHigher = [AppRole.SystemAdmin, AppRole.ClientAdmin, AppRole.Supervisor];
    private static readonly AppRole[] AnyRole = [AppRole.SystemAdmin, AppRole.ClientAdmin, AppRole.Supervisor, AppRole.Employee];

    public static AuthorizationBuilder AddRolePolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(SystemAdmin, p => p.RequireClaim(TenantClaimTypes.Role, RoleNames(SystemAdminOrHigher)))
        .AddPolicy(ClientAdmin, p => p.RequireClaim(TenantClaimTypes.Role, RoleNames(ClientAdminOrHigher)))
        .AddPolicy(Supervisor, p => p.RequireClaim(TenantClaimTypes.Role, RoleNames(SupervisorOrHigher)))
        .AddPolicy(Employee, p => p.RequireClaim(TenantClaimTypes.Role, RoleNames(AnyRole)));

    private static string[] RoleNames(AppRole[] roles) => [.. roles.Select(r => r.ToString())];
}
