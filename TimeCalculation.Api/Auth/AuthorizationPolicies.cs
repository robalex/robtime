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
/// Not yet applied to any endpoint — landing the infrastructure ahead of the endpoint-by-endpoint
/// retrofit (UI_PLAN.md Phase 1) so it exists to build against without a large simultaneous change
/// to every existing CRUD endpoint and its tests.
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
