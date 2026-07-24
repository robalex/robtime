using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// One real Postgres (via Testcontainers, not a mock/in-memory provider — this is meant to catch
/// exactly the kind of thing that only shows up against the real engine, like the [AsParameters]
/// paging bug and the Punch.ClientId FK bug found by hand during this same pass of work) shared
/// across every test in the "Api" collection, migrated once at startup. Tests share the database,
/// not a transaction-per-test rollback — give created rows unique names/values rather than
/// assuming an empty table.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>Unauthenticated — carries none of the TestAuthHandler headers, so it exercises
    /// exactly what a request with no bearer token gets against the real Cognito scheme (401 on
    /// anything RequireAuthorization()-protected).</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Authenticated as SystemAdmin with no selected client (ClientId claim absent) — the
    /// one identity allowed to call the cross-tenant Client list/create endpoints (UI_PLAN.md §5's
    /// "SystemAdmin scoping" decision). Safe to share across tests: this identity never varies per
    /// test, unlike the per-test ClientAdmin identities from <see cref="CreateClientAndScopedClientAsync"/>.</summary>
    public HttpClient SystemAdminClient { get; private set; } = null!;

    /// <summary>The factory's DI container — for tests that need to reach into the real database
    /// directly (e.g. flipping a PayRule to Active, which has no API path yet).</summary>
    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException($"{nameof(ApiFixture)} not initialized yet.");

    /// <summary>For tests that need a standalone PayrollDbContext with a specific
    /// ITenantContextAccessor (e.g. TenantIsolationTests) — built directly from this connection
    /// string rather than resolved through DI, since DbContextOptions resolved via
    /// AddDbContext ties itself to the originating DI scope internally and breaks once that scope is
    /// disposed. Same pattern PayrollDbContextFactory/PersistenceModelTests already use.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PayrollDb"] = _postgres.GetConnectionString(),
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Overrides Program.cs's default scheme ("Bearer", real Cognito JwtBearer) with the
                // fake one for the whole test host — see TestAuthHandler's own doc comment for why
                // there's no Testcontainers-equivalent to spin up a real Cognito pool instead.
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });

        Client = _factory.CreateClient();
        SystemAdminClient = CreateAuthenticatedClient(AppRole.SystemAdmin, clientId: null, sub: "test-system-admin");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>A new HttpClient (same in-memory TestServer, its own header set — safe to mutate
    /// without racing other tests, unlike adding headers to a shared client) authenticated as the
    /// given identity via TestAuthHandler.</summary>
    public HttpClient CreateAuthenticatedClient(AppRole role, int? clientId, string sub)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role.ToString());
        if (clientId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ClientIdHeader, clientId.Value.ToString());
        }

        return client;
    }

    /// <summary>The pattern every test needs: create a fresh Client (SystemAdmin-only) and get back
    /// an HttpClient already authenticated as that Client's ClientAdmin for everything else the test
    /// does — Employee/Position/PayRule/Punch CRUD is normally tenant-scoped, so it needs a principal
    /// whose ClientId claim actually matches the client just created, not the SystemAdmin identity
    /// used to create it.</summary>
    public async Task<(int ClientId, HttpClient Api)> CreateClientAndScopedClientAsync(string name, CancellationToken ct)
    {
        var request = new CreateClientRequest { Name = name };
        var response = await SystemAdminClient.PostAsJsonAsync("/clients", request, TestJson.Options, ct);
        response.EnsureSuccessStatusCode();
        var client = (await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, ct))!;
        return (client.Id, CreateAuthenticatedClient(AppRole.ClientAdmin, client.Id, sub: $"test-client-admin-{client.Id}"));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
