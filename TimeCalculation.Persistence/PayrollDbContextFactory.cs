using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TimeCalculation.Persistence;

/// <summary>
/// Design-time factory used only by the `dotnet ef` tooling.
///
/// `PayrollDbContext` has no parameterless constructor, so without this EF would fall back to
/// booting a startup project's host to pull the context out of DI — which is why migrations used to
/// need `--startup-project TimeCalculation.Api`. Persistence owns the schema; it has no business
/// reaching through the API to describe it. This factory makes the project self-sufficient.
///
/// The target database is whichever connection string is supplied, which is deliberately explicit:
/// for a schema-altering command, naming the database beats inheriting an ambient environment.
/// Resolution order:
///   1. ROBTIME_PAYROLL_DB              — the intended knob for non-local targets
///   2. ConnectionStrings__PayrollDb    — the same variable the API honours, for a shared shell
///   3. the local development database  — so day-to-day local work stays zero-config
///
/// Defaulting to local is the safe failure mode: forgetting to set the variable migrates your own
/// machine, never someone else's environment.
///
/// This type is never used at runtime — the API supplies its own options from configuration.
/// </summary>
public sealed class PayrollDbContextFactory : IDesignTimeDbContextFactory<PayrollDbContext>
{
    public const string ConnectionStringVariable = "ROBTIME_PAYROLL_DB";

    private const string LocalDevelopmentDatabase =
        "Host=localhost;Port=5432;Database=robtime;Username=postgres;Password=postgres";

    public PayrollDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__PayrollDb")
            ?? LocalDevelopmentDatabase;

        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime())
            .Options;

        // No tenant: migrations describe the schema, which is tenant-agnostic.
        return new PayrollDbContext(options);
    }
}
