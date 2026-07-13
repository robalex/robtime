using Microsoft.EntityFrameworkCore;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

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
            b.HasQueryFilter(e => _tenantClientId == null || e.ClientId == _tenantClientId);
        });

        model.Entity<Position>(b =>
        {
            b.ToTable("positions");
            b.HasKey(p => p.Id);
            b.Property(p => p.BaseRate).HasPrecision(19, 4);
            b.HasQueryFilter(p => _tenantClientId == null || p.ClientId == _tenantClientId);
        });

        model.Entity<Punch>(b =>
        {
            b.ToTable("punches");
            b.HasKey(p => p.Id);

            // Hot index for effective-dated punch lookups.
            b.HasIndex(p => new { p.EmployeeId, p.PunchTime });

            // Device idempotency: at most one punch per (employee, device, device punch id).
            b.HasIndex(p => new { p.EmployeeId, p.DeviceId, p.DevicePunchId })
                .IsUnique()
                .HasFilter(null);

            b.Property(p => p.Hours).HasPrecision(10, 4);   // hours quantity
            b.Property(p => p.Amount).HasPrecision(19, 4);  // money

            b.HasOne(p => p.Employee).WithMany().HasForeignKey(p => p.EmployeeId);
            b.HasOne(p => p.Position).WithMany().HasForeignKey(p => p.PositionId);

            // Soft delete: deleted punches are invisible to all queries.
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        model.Entity<PunchAuditEntry>(b =>
        {
            b.ToTable("punch_audits");
            b.HasKey(a => a.Id);
            b.HasIndex(a => a.PunchId);
        });

        model.Entity<PayRule>(b =>
        {
            b.ToTable("pay_rules");
            b.HasKey(r => r.Id);
            b.OwnsOne(r => r.RoundingRule);
            b.OwnsOne(r => r.OvertimeRule, o =>
            {
                o.Property(x => x.WeeklyOvertimeThresholdHours).HasPrecision(10, 4);
                o.Property(x => x.DailyOvertimeThresholdHours).HasPrecision(10, 4);
                o.Property(x => x.DailyDoubletimeThresholdHours).HasPrecision(10, 4);
            });

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
        });

        model.Entity<PayRuleAssignmentEntity>(b =>
        {
            b.ToTable("pay_rule_assignments");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.EmployeeId, a.EffectiveFrom });   // effective-dated resolution index
            b.HasOne(a => a.PayRule).WithMany().HasForeignKey(a => a.PayRuleId);
        });

        model.Entity<EmployeePositionAssignmentEntity>(b =>
        {
            b.ToTable("employee_position_assignments");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.EmployeeId, a.EffectiveFrom });
            b.HasOne(a => a.Position).WithMany().HasForeignKey(a => a.PositionId);
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
