using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Proves UI_PLAN.md §5's tenant-isolation requirement at the persistence layer, not just through
/// the HTTP surface: a row created for one client must be invisible to a PayrollDbContext scoped to
/// a different client. Table-driven over every tenant-scoped entity type in PayrollDbContext, so a
/// future entity that forgets to add (or mis-wires) its tenant filter fails this test instead of
/// shipping a cross-tenant leak. Runs directly against the DbContext (not through the API), which is
/// both simpler than standing up full HTTP call chains for entities with no CRUD endpoints yet
/// (PunchAuditEntry, the assignment entities) and a more direct test of the actual mechanism (the
/// query filter) than an HTTP round-trip would be.
/// </summary>
[Collection("Api")]
public class TenantIsolationTests(ApiFixture fixture)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 1, 0, 0);

    public static IEnumerable<object[]> TenantScopedEntities()
    {
        yield return
        [
            "Client",
            (SeedFunc)(async (db, tenantId) =>
            {
                // The Client row IS the tenant — nothing extra to insert.
                await Task.CompletedTask;
                return tenantId.ToString();
            }),
            (ExistsFunc)((db, id) => db.Clients.AnyAsync(c => c.Id == int.Parse(id))),
        ];

        yield return
        [
            "Employee",
            (SeedFunc)(async (db, tenantId) =>
            {
                var employee = new Employee { ClientId = tenantId, FirstName = "Iso", LastName = "Test", MinimumWage = 15m };
                db.Employees.Add(employee);
                await db.SaveChangesAsync();
                return employee.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.Employees.AnyAsync(e => e.Id == int.Parse(id))),
        ];

        yield return
        [
            "Position",
            (SeedFunc)(async (db, tenantId) =>
            {
                var position = new Position { ClientId = tenantId, Code = "ISO", Name = "Iso Test", BaseRate = 15m };
                db.Positions.Add(position);
                await db.SaveChangesAsync();
                return position.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.Positions.AnyAsync(p => p.Id == int.Parse(id))),
        ];

        yield return
        [
            "PayRule",
            (SeedFunc)(async (db, tenantId) =>
            {
                var payRule = new PayRule { ClientId = tenantId, Name = "Iso Test" };
                db.PayRules.Add(payRule);
                await db.SaveChangesAsync();
                return payRule.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.PayRules.AnyAsync(r => r.Id == int.Parse(id))),
        ];

        yield return
        [
            "Punch",
            (SeedFunc)(async (db, tenantId) =>
            {
                var employee = new Employee { ClientId = tenantId, FirstName = "Iso", LastName = "Punch", MinimumWage = 15m };
                db.Employees.Add(employee);
                await db.SaveChangesAsync();
                var punch = new Punch
                {
                    ClientId = tenantId, EmployeeId = employee.Id, PunchTime = Now, Kind = PunchKind.In, CreatedAt = Now,
                };
                db.Punches.Add(punch);
                await db.SaveChangesAsync();
                return punch.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.Punches.AnyAsync(p => p.Id == int.Parse(id))),
        ];

        yield return
        [
            "PunchAuditEntry",
            (SeedFunc)(async (db, tenantId) =>
            {
                var entry = new PunchAuditEntry
                {
                    ClientId = tenantId, PunchId = 0, ActorUserId = "iso-test", OccurredAt = Now, Action = "Created",
                };
                db.PunchAudits.Add(entry);
                await db.SaveChangesAsync();
                return entry.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.PunchAudits.AnyAsync(a => a.Id == int.Parse(id))),
        ];

        yield return
        [
            "DifferentialRule",
            (SeedFunc)(async (db, tenantId) =>
            {
                var rule = new DifferentialRule { ClientId = tenantId, Code = "ISO" };
                db.DifferentialRules.Add(rule);
                await db.SaveChangesAsync();
                return rule.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.DifferentialRules.AnyAsync(d => d.Id == int.Parse(id))),
        ];

        yield return
        [
            "HolidayCalendar",
            (SeedFunc)(async (db, tenantId) =>
            {
                var calendar = new HolidayCalendar { ClientId = tenantId, Name = "Iso Test" };
                db.HolidayCalendars.Add(calendar);
                await db.SaveChangesAsync();
                return calendar.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.HolidayCalendars.AnyAsync(h => h.Id == int.Parse(id))),
        ];

        yield return
        [
            "ClientPremiumPolicy",
            (SeedFunc)(async (db, tenantId) =>
            {
                var policy = new ClientPremiumPolicy
                {
                    ClientId = tenantId, PremiumCode = "CA_MEAL", WaiverPolicy = WaiverPolicy.NotWaivable,
                    SetBy = "iso-test", SetAt = Now, EffectiveFrom = Now.InUtc().Date,
                };
                db.ClientPremiumPolicies.Add(policy);
                await db.SaveChangesAsync();
                return policy.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.ClientPremiumPolicies.AnyAsync(c => c.Id == int.Parse(id))),
        ];

        yield return
        [
            "PayRuleAssignment",
            (SeedFunc)(async (db, tenantId) =>
            {
                var employee = new Employee { ClientId = tenantId, FirstName = "Iso", LastName = "Assign", MinimumWage = 15m };
                var payRule = new PayRule { ClientId = tenantId, Name = "Iso Test" };
                db.Employees.Add(employee);
                db.PayRules.Add(payRule);
                await db.SaveChangesAsync();
                var assignment = new PayRuleAssignmentEntity
                {
                    ClientId = tenantId, EmployeeId = employee.Id, PayRuleId = payRule.Id, EffectiveFrom = Now.InUtc().Date,
                };
                db.PayRuleAssignments.Add(assignment);
                await db.SaveChangesAsync();
                return assignment.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.PayRuleAssignments.AnyAsync(a => a.Id == int.Parse(id))),
        ];

        yield return
        [
            "EmployeePositionAssignment",
            (SeedFunc)(async (db, tenantId) =>
            {
                var employee = new Employee { ClientId = tenantId, FirstName = "Iso", LastName = "Assign", MinimumWage = 15m };
                var position = new Position { ClientId = tenantId, Code = "ISO", Name = "Iso Test", BaseRate = 15m };
                db.Employees.Add(employee);
                db.Positions.Add(position);
                await db.SaveChangesAsync();
                var assignment = new EmployeePositionAssignmentEntity
                {
                    ClientId = tenantId, EmployeeId = employee.Id, PositionId = position.Id, EffectiveFrom = Now.InUtc().Date,
                };
                db.EmployeePositionAssignments.Add(assignment);
                await db.SaveChangesAsync();
                return assignment.Id.ToString();
            }),
            (ExistsFunc)((db, id) => db.EmployeePositionAssignments.AnyAsync(a => a.Id == int.Parse(id))),
        ];

        yield return
        [
            "AppUser",
            (SeedFunc)(async (db, tenantId) =>
            {
                var sub = $"iso-test-{Guid.NewGuid()}";
                db.AppUsers.Add(new AppUser { CognitoSub = sub, ClientId = tenantId, DisplayName = "Iso Test", Role = AppRole.ClientAdmin });
                await db.SaveChangesAsync();
                return sub;
            }),
            (ExistsFunc)((db, id) => db.AppUsers.AnyAsync(u => u.CognitoSub == id)),
        ];
    }

    public delegate Task<string> SeedFunc(PayrollDbContext db, int tenantId);
    public delegate Task<bool> ExistsFunc(PayrollDbContext db, string id);

    [Theory]
    [MemberData(nameof(TenantScopedEntities))]
    public async Task Entity_CreatedUnderOneTenant_InvisibleUnderAnotherTenant(string entityName, SeedFunc seed, ExistsFunc exists)
    {
        var (tenantA, _) = await fixture.CreateClientAndScopedClientAsync($"Iso A {entityName} {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (tenantB, _) = await fixture.CreateClientAndScopedClientAsync($"Iso B {entityName} {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Inserts are never filtered (query filters only apply to reads), so an unrestricted write
        // context can seed data for tenant A regardless of its own tenant setting.
        await using var writeDb = CreateContext(null);
        var id = await seed(writeDb, tenantA);

        await using var ownTenantDb = CreateContext(tenantA);
        Assert.True(await exists(ownTenantDb, id), $"{entityName}: tenant A should see its own row.");

        await using var otherTenantDb = CreateContext(tenantB);
        Assert.False(await exists(otherTenantDb, id), $"{entityName}: tenant B must NOT see tenant A's row — cross-tenant leak.");
    }

    [Fact]
    public void PunchQuery_GeneratedSql_FiltersClientIdAsPlainEquality()
    {
        // Punches are the hottest, largest table in the system (PayrollDbContext's own class doc
        // comment) — this is the sargability regression guard UI_PLAN.md §5 calls for: a leading
        // `_tenantClientId == null || ...` branch defeats index usage once Postgres falls back to a
        // generic query plan, so the generated SQL must show a plain `client_id = @tenant` equality,
        // never an OR.
        using var db = CreateContext(42);
        var sql = db.Punches.Where(p => p.EmployeeId == 1).ToQueryString();

        Assert.Contains("client_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmployeeQuery_GeneratedSql_FiltersClientIdAsPlainEquality()
    {
        using var db = CreateContext(42);
        var sql = db.Employees.Where(e => e.LastName == "Test").ToQueryString();

        Assert.Contains("client_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR ", sql, StringComparison.OrdinalIgnoreCase);
    }

    // Cached per connection string (static — xunit constructs a new TenantIsolationTests instance
    // per test method, but ApiFixture's Testcontainers connection string is stable for the whole
    // run) and reused across every CreateContext call. Rebuilding a fresh
    // DbContextOptionsBuilder().UseNpgsql(...) per call means a fresh underlying NpgsqlDataSource/
    // connection pool each time too, which piled up fast enough across ~12 theory cases × 3 contexts
    // each to start intermittently failing. One shared options instance per connection string, one pool.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DbContextOptions<PayrollDbContext>> OptionsCache = new();

    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = OptionsCache.GetOrAdd(fixture.ConnectionString, cs => new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.UseNodaTime())
            .Options);
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
