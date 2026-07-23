using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
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

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The factory's DI container — for tests that need to reach into the real database
    /// directly (e.g. flipping a PayRule to Active, which has no API path yet).</summary>
    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException($"{nameof(ApiFixture)} not initialized yet.");

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
        });

        Client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.MigrateAsync();
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
