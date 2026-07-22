using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Persistence;

/// <summary>
/// EF Core code-first context (Npgsql + NodaTime).  Encodes the persistence design from the plan:
/// decimal precision (money 19,4 / hours 10,4), the hot indexes, NodaTime instant/date mapping,
/// soft-delete and multi-tenant global query filters, and the device idempotency constraint.
///
/// This project depends only on TimeCalculation.Model (the pure entity/config types), not the
/// TimeCalculation calculation engine — persistence is a data-access concern, not a place to run
/// PayCalculator. A future API/worker project composes both: TimeCalculation for calculation,
/// this project for storage, TimeCalculation.Model for the shapes they share.
///
/// Not encoded here (deferred / open decisions): table partitioning by year (Postgres DDL, applied
/// in a migration), the worker-queue choice for parallel period calculation (open decision #6), and
/// JSON storage of PayCalculationSnapshot.
/// </summary>
public class PayrollDbContext : DbContext
{
    private readonly int? _tenantClientId;

    public PayrollDbContext(DbContextOptions<PayrollDbContext> options, int? tenantClientId = null)
        : base(options) => _tenantClientId = tenantClientId;

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<PunchAuditEntry> PunchAudits => Set<PunchAuditEntry>();
    public DbSet<PayRule> PayRules => Set<PayRule>();
    public DbSet<PayRuleAssignmentEntity> PayRuleAssignments => Set<PayRuleAssignmentEntity>();
    public DbSet<EmployeePositionAssignmentEntity> EmployeePositionAssignments => Set<EmployeePositionAssignmentEntity>();
    public DbSet<StateMinimumWage> StateMinimumWages => Set<StateMinimumWage>();
    public DbSet<DifferentialRule> DifferentialRules => Set<DifferentialRule>();
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();
    public DbSet<ClientPremiumPolicy> ClientPremiumPolicies => Set<ClientPremiumPolicy>();

    /// <summary>
    /// Snake-cases every generated identifier so columns match the already snake_cased table names
    /// (`punch_time`, not `"PunchTime"`) and hand-written SQL never needs quoting. Applied here
    /// rather than in the composition root so the model is identical however the context is built —
    /// the API host, the tests, and `dotnet ef` design-time all produce the same schema.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSnakeCaseNamingConvention();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Money default; hour quantities are overridden to (10,4) per property below.
        builder.Properties<decimal>().HavePrecision(19, 4);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Client>(b =>
        {
            b.ToTable("clients");
            b.HasKey(c => c.Id);
            b.HasQueryFilter(c => _tenantClientId == null || c.Id == _tenantClientId);
        });

        model.Entity<Employee>(b =>
        {
            b.ToTable("employees");
            b.HasKey(e => e.Id);
            b.Property(e => e.MinimumWage).HasPrecision(19, 4);
            // No navigation on the entity — TimeCalculation.Model stays pure data, so the FK is
            // declared without one. Restrict, not Cascade: deleting a client must never silently
            // take payroll records with it.
            b.HasOne<Client>().WithMany().HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(e => _tenantClientId == null || e.ClientId == _tenantClientId);
        });

        model.Entity<Position>(b =>
        {
            b.ToTable("positions");
            b.HasKey(p => p.Id);
            b.Property(p => p.BaseRate).HasPrecision(19, 4);
            b.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(p => _tenantClientId == null || p.ClientId == _tenantClientId);
        });

        model.Entity<Punch>(b =>
        {
            b.ToTable("punches");
            b.HasKey(p => p.Id);

            // Hot index for effective-dated punch lookups, client_id leading (tenancy schema prep —
            // see PayrollDbContext's class doc comment; the tenant query filter itself is Phase 1
            // work, added once auth resolves a real _tenantClientId. A leading == null || ... filter
            // isn't sargable under a generic query plan, so ClientId needs to be first in the index
            // regardless of when the filter predicate lands, or the index is useless the day it does.
            b.HasIndex(p => new { p.ClientId, p.EmployeeId, p.PunchTime });

            // Device idempotency: at most one punch per (employee, device, device punch id).
            b.HasIndex(p => new { p.EmployeeId, p.DeviceId, p.DevicePunchId })
                .IsUnique()
                .HasFilter(null);

            b.Property(p => p.Hours).HasPrecision(10, 4);   // hours quantity
            b.Property(p => p.Amount).HasPrecision(19, 4);  // money

            b.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.Employee).WithMany().HasForeignKey(p => p.EmployeeId);
            b.HasOne(p => p.Position).WithMany().HasForeignKey(p => p.PositionId);

            // Soft delete: deleted punches are invisible to all queries. No tenant filter yet — see
            // the index comment above; adding one is Phase 1, once ClientId is populated and auth
            // can supply a real _tenantClientId.
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        model.Entity<PunchAuditEntry>(b =>
        {
            b.ToTable("punch_audits");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.ClientId, a.PunchId });
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayRule>(b =>
        {
            b.ToTable("pay_rules");
            b.HasKey(r => r.Id);

            // Version-history lookup: "every version of this rule family" / "the currently Active
            // one." See PayRule's own doc comments on RuleFamilyId/Version/Status for the versioning
            // design — never mutate an Active row, always insert a new one.
            b.HasIndex(r => r.RuleFamilyId);

            b.OwnsOne(r => r.RoundingRule);
            b.OwnsOne(r => r.OvertimeRule, o =>
            {
                o.Property(x => x.WeeklyOvertimeThresholdHours).HasPrecision(10, 4);
                o.Property(x => x.DailyOvertimeThresholdHours).HasPrecision(10, 4);
                o.Property(x => x.DailyDoubletimeThresholdHours).HasPrecision(10, 4);
            });

            b.HasOne<Client>().WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);

            b.Property(r => r.PunchPairResetHours).HasPrecision(10, 4);
            b.Property(r => r.MaxShiftLengthHours).HasPrecision(10, 4);
            b.Property(r => r.DistanceBetweenShiftsHours).HasPrecision(10, 4);

            // Set<string> stored as a comma-delimited column (small, read-mostly).
            b.Property(r => r.ActivePremiumCodes)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0
                        ? new HashSet<string>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<string>>(
                        (a, c) => a!.SetEquals(c!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToHashSet()));

            // Same treatment as ActivePremiumCodes, but selecting from the client's own
            // (client-authored) DifferentialRule.Code values rather than a fixed built-in registry.
            b.Property(r => r.ActiveDifferentialCodes)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0
                        ? new HashSet<string>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<string>>(
                        (a, c) => a!.SetEquals(c!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToHashSet()));
        });

        model.Entity<DifferentialRule>(b =>
        {
            b.ToTable("differential_rules");
            b.HasKey(d => d.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(d => d.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(d => _tenantClientId == null || d.ClientId == _tenantClientId);

            b.Property(d => d.MinHoursInWindow).HasPrecision(10, 4);
            b.Property(d => d.MinHoursInRange).HasPrecision(10, 4);
            // AdjustmentValue is left at the (19,4) money default: DifferentialAdjustmentType decides
            // whether it's a dollar amount or a multiplier, and (19,4) comfortably fits either.

            b.Property(d => d.DaysOfWeek)
                .HasConversion(
                    v => string.Join(',', v.Select(x => x.ToString())),
                    v => v.Length == 0
                        ? new HashSet<IsoDayOfWeek>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<IsoDayOfWeek>).ToHashSet())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<IsoDayOfWeek>>(
                        (a, c) => a!.SetEquals(c!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToHashSet()));

            b.Property(d => d.SpecificDates)
                .HasConversion(
                    v => string.Join(',', v.Select(LocalDatePattern.Iso.Format)),
                    v => v.Length == 0
                        ? new HashSet<LocalDate>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => LocalDatePattern.Iso.Parse(s).Value).ToHashSet())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<LocalDate>>(
                        (a, c) => a!.SetEquals(c!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToHashSet()));
        });

        model.Entity<HolidayCalendar>(b =>
        {
            b.ToTable("holiday_calendars");
            b.HasKey(h => h.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(h => h.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(h => _tenantClientId == null || h.ClientId == _tenantClientId);

            b.Property(h => h.Dates)
                .HasConversion(
                    v => string.Join(',', v.Select(LocalDatePattern.Iso.Format)),
                    v => v.Length == 0
                        ? new HashSet<LocalDate>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => LocalDatePattern.Iso.Parse(s).Value).ToHashSet())
                .Metadata.SetValueComparer(
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<LocalDate>>(
                        (a, c) => a!.SetEquals(c!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToHashSet()));
        });

        model.Entity<ClientPremiumPolicy>(b =>
        {
            b.ToTable("client_premium_policies");
            b.HasKey(c => c.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(c => _tenantClientId == null || c.ClientId == _tenantClientId);

            // Resolution lookup: "this client's policy for this premium code, as of a given date."
            b.HasIndex(c => new { c.ClientId, c.PremiumCode, c.EffectiveFrom });
        });

        model.Entity<PayRuleAssignmentEntity>(b =>
        {
            b.ToTable("pay_rule_assignments");
            b.HasKey(a => a.Id);
            // client_id leading (tenancy schema prep — see the Punch index comment above; the
            // filter predicate itself is Phase 1).
            b.HasIndex(a => new { a.ClientId, a.EmployeeId, a.EffectiveFrom });
            b.HasOne(a => a.PayRule).WithMany().HasForeignKey(a => a.PayRuleId);
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<EmployeePositionAssignmentEntity>(b =>
        {
            b.ToTable("employee_position_assignments");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.ClientId, a.EmployeeId, a.EffectiveFrom });
            b.HasOne(a => a.Position).WithMany().HasForeignKey(a => a.PositionId);
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.Property(a => a.Rate).HasPrecision(19, 4);

            // Position is tenant-filtered and is the REQUIRED end of this relationship, so without a
            // matching filter an assignment could survive a query whose Position was filtered away,
            // leaving a required navigation unsatisfiable. Filtering through the navigation keeps
            // both ends consistent. Still keyed off Position.ClientId, not the entity's own new
            // ClientId column, deliberately — switching the filter's predicate is Phase 1's "rework
            // the tenant filters" pass, not this one; the column exists now so that pass has
            // something to switch to.
            b.HasQueryFilter(a => _tenantClientId == null || a.Position.ClientId == _tenantClientId);
        });

        model.Entity<StateMinimumWage>(b =>
        {
            b.ToTable("state_minimum_wages");
            b.HasKey(s => s.Id);
            b.Property(s => s.Amount).HasPrecision(19, 4);
            b.HasIndex(s => new { s.State, s.EffectiveFrom });
        });
    }
}
