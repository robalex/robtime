using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.EntityFrameworkCore;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api;

/// <summary>
/// Solves the bootstrap chicken-and-egg problem: <c>POST /users</c> requires an already-authorized
/// caller, but the very first SystemAdmin in a new environment has no <see cref="AppUser"/> row yet
/// to be authorized with. Run via <c>dotnet run -- --bootstrap-admin &lt;email&gt;</c> against a
/// Cognito user already created in the console (RobTimeUI/README.md's "Local configuration" step 4).
/// Local-dev convenience, not a real deployment path — same spirit as <see cref="DevSeeder"/>.
///
/// Links that Cognito identity to a local AppUser row and ensures its <c>custom:role</c> attribute
/// is SystemAdmin; it never touches a password or any other credential.
/// </summary>
public static class AdminBootstrapper
{
    public static async Task RunAsync(
        IAmazonCognitoIdentityProvider cognito, PayrollDbContext db, string userPoolId, string email, CancellationToken ct)
    {
        AdminGetUserResponse cognitoUser;
        try
        {
            cognitoUser = await cognito.AdminGetUserAsync(
                new AdminGetUserRequest { UserPoolId = userPoolId, Username = email }, ct);
        }
        catch (UserNotFoundException)
        {
            Console.WriteLine($"No Cognito user found for '{email}' in pool {userPoolId}. Create it in the console first.");
            return;
        }

        var sub = cognitoUser.UserAttributes.FirstOrDefault(a => a.Name == "sub")?.Value
            ?? throw new InvalidOperationException($"Cognito user '{email}' has no 'sub' attribute.");

        // The role claim lives on Cognito, not in AppUser — a role check reads the JWT, never the
        // local row (see CurrentUserService's doc comment) — so this is fixed here too, not just the
        // AppUser row below. Console-created users commonly have this unset.
        var currentRole = cognitoUser.UserAttributes.FirstOrDefault(a => a.Name == "custom:role")?.Value;
        if (currentRole != nameof(AppRole.SystemAdmin))
        {
            await cognito.AdminUpdateUserAttributesAsync(new AdminUpdateUserAttributesRequest
            {
                UserPoolId = userPoolId,
                Username = email,
                UserAttributes = [new AttributeType { Name = "custom:role", Value = nameof(AppRole.SystemAdmin) }],
            }, ct);
            Console.WriteLine($"Set custom:role=SystemAdmin on the Cognito user (was '{currentRole ?? "<none>"}').");
        }

        // IgnoreQueryFilters: no request/tenant is behind this CLI invocation, so there's no
        // _tenantClientId to filter by — the same escape hatch ClientService.ListAsync uses.
        var existing = await db.AppUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.CognitoSub == sub, ct);
        if (existing is null)
        {
            db.AppUsers.Add(new AppUser
            {
                CognitoSub = sub,
                ClientId = null,
                Role = AppRole.SystemAdmin,
                DisplayName = email[..email.IndexOf('@')],
            });
            await db.SaveChangesAsync(ct);
            Console.WriteLine($"Created AppUser row for '{email}' ({sub}) as SystemAdmin.");
        }
        else
        {
            Console.WriteLine($"AppUser row already exists for '{email}' ({sub}), role {existing.Role}.");
        }

        Console.WriteLine("Sign out and back in — an already-issued token won't pick up the role change.");
    }
}
