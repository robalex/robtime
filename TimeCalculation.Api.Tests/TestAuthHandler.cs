using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeCalculation.Api.Auth;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Stands in for real Cognito JWT validation in tests — there's no Testcontainers-equivalent for
/// Cognito (UI_PLAN.md §5's "no live Cognito pool per test run" point), so this reads the same
/// claims a validated token would carry straight off request headers instead. Registered as the
/// default scheme only inside ApiFixture's WebApplicationFactory (see ConfigureTestServices there),
/// never in the real app — Program.cs always configures the real JwtBearer/Cognito scheme.
///
/// A request with none of the test headers authenticates as nobody (NoResult), exactly like a
/// request with no bearer token against the real scheme — so RequireAuthorization()-protected
/// endpoints still 401 an unauthenticated test request.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";
    public const string ClientIdHeader = "X-Test-ClientId";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(TenantClaimTypes.Sub, sub!) };
        if (Request.Headers.TryGetValue(ClientIdHeader, out var clientId))
        {
            claims.Add(new Claim(TenantClaimTypes.ClientId, clientId!));
        }

        if (Request.Headers.TryGetValue(RoleHeader, out var role))
        {
            claims.Add(new Claim(TenantClaimTypes.Role, role!));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
