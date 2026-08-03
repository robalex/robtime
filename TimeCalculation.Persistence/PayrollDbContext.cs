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
/// in a migration), and the worker-queue choice for parallel period calculation (open decision #6).
/// PayCalculationSnapshot's JSON storage landed in Phase 6.7 — see TimecardApproval.SnapshotJson.
/// </summary>
public class PayrollDbContext : DbContext
{
    private readonly int? _tenantClientId;

    /// <summary>
    /// <paramref name="tenantContextAccessor"/> is resolved via DI in the API host — see
    /// <c>HttpContextTenantContextAccessor</c> — and reads `client_id` off the validated Cognito JWT
    /// (UI_PLAN.md §5). Optional so `dotnet ef` design-time tooling and migrations, which describe a
    /// tenant-agnostic schema, can keep constructing a context with no accessor at all.
    /// </summary>
    public PayrollDbContext(DbContextOptions<PayrollDbContext> options, ITenantContextAccessor? tenantContextAccessor = null)
        : base(options) => _tenantClientId = tenantContextAccessor?.ClientId;

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
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
    public DbSet<ClientSettings> ClientSettings => Set<ClientSettings>();
    public DbSet<PunchChangeRequest> PunchChangeRequests => Set<PunchChangeRequest>();
    public DbSet<TimecardApproval> TimecardApprovals => Set<TimecardApproval>();
    public DbSet<PunchImportBatch> PunchImportBatches => Set<PunchImportBatch>();
    public DbSet<PayrollExportProfile> PayrollExportProfiles => Set<PayrollExportProfile>();
    public DbSet<PayrollEarningCodeMapping> PayrollEarningCodeMappings => Set<PayrollEarningCodeMapping>();
    public DbSet<PayrollEmployeeIdentifier> PayrollEmployeeIdentifiers => Set<PayrollEmployeeIdentifier>();

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
            // Two independent, named filters (EF Core 10) — tenant scoping and soft-delete are
            // orthogonal concerns and read better apart than combined into one lambda. EF ANDs
            // every named filter together automatically; verified this empirically (see
            // PersistenceModelTests) rather than assuming from the docs alone.
            //
            // Plain equality, no `_tenantClientId == null ||` escape hatch (UI_PLAN.md §5, Phase 1
            // "rework the tenant filters"): that OR-branch is exactly what stops Postgres from using
            // a sargable index scan once it switches to a generic query plan. A context built with no
            // tenant (_tenantClientId null — DevSeeder, `dotnet ef`, an unauthenticated request) now
            // filters every tenant-scoped query down to nothing instead of everything — a fail-closed
            // default. The one legitimate "no tenant" case (SystemAdmin listing/creating clients) is
            // served by IgnoreQueryFilters() at that specific call site (see ClientService.ListAsync),
            // not by a bypass baked into the predicate.
            b.HasQueryFilter("Tenant", c => c.Id == _tenantClientId);
            b.HasQueryFilter("SoftDelete", c => !c.IsDeleted);
        });

        model.Entity<AppUser>(b =>
        {
            b.ToTable("app_users");
            b.HasKey(u => u.CognitoSub);
            b.Property(u => u.CognitoSub).HasMaxLength(64);   // Cognito sub is a UUID
            b.HasIndex(u => u.ClientId);
            b.HasOne<Client>().WithMany().HasForeignKey(u => u.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(u => u.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(u => u.ClientId == _tenantClientId);
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
            b.HasQueryFilter("Tenant", e => e.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", e => !e.IsDeleted);
        });

        model.Entity<Position>(b =>
        {
            b.ToTable("positions");
            b.HasKey(p => p.Id);
            b.Property(p => p.BaseRate).HasPrecision(19, 4);
            b.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter("Tenant", p => p.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", p => !p.IsDeleted);
        });

        model.Entity<Punch>(b =>
        {
            b.ToTable("punches");
            b.HasKey(p => p.Id);

            // Hot index for effective-dated punch lookups, client_id leading — matches the plain
            // equality tenant filter below, so this stays sargable under a generic query plan too.
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
            // Restrict, not a real concern in practice: PunchImportBatch rows are never deleted
            // (DeleteBatchAsync hard-deletes the punches themselves, not their parent batch), so this
            // FK is never actually asked to cascade or block anything — it's here for referential
            // integrity, not because a delete path exercises it.
            b.HasOne<PunchImportBatch>().WithMany().HasForeignKey(p => p.ImportBatchId).OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter("Tenant", p => p.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", p => !p.IsDeleted);
        });

        model.Entity<PunchAuditEntry>(b =>
        {
            b.ToTable("punch_audits");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.ClientId, a.PunchId });
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(a => a.ClientId == _tenantClientId);
        });

        model.Entity<PunchChangeRequest>(b =>
        {
            b.ToTable("punch_change_requests");
            b.HasKey(r => r.Id);
            // Pending-queue lookup (Phase 6.6): "this client's pending requests," status leading
            // since that's always in the query, then EmployeeId for the timecard's own filtered view.
            b.HasIndex(r => new { r.ClientId, r.Status, r.EmployeeId });

            b.Property(r => r.RequestedAmount).HasPrecision(19, 4);
            b.Property(r => r.RequestedHours).HasPrecision(10, 4);

            b.HasOne<Client>().WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            // No FK to Punch — PunchId is null for a pending Add request (there's no punch yet), so
            // this can't be a required relationship, and EF's optional-FK-to-a-record-type support is
            // more friction than a plain unconstrained int buys here. The punch_audits table takes the
            // same approach for the same reason once a punch is soft-deleted out from under an entry.
            b.HasQueryFilter(r => r.ClientId == _tenantClientId);
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
            b.HasOne<HolidayCalendar>().WithMany().HasForeignKey(r => r.HolidayCalendarId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter("Tenant", r => r.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", r => !r.IsDeleted);

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
            b.HasQueryFilter("Tenant", d => d.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", d => !d.IsDeleted);

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
            b.HasQueryFilter("Tenant", h => h.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", h => !h.IsDeleted);

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
            b.HasQueryFilter("Tenant", c => c.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", c => !c.IsDeleted);

            // Resolution lookup: "this client's policy for this premium code, as of a given date."
            b.HasIndex(c => new { c.ClientId, c.PremiumCode, c.EffectiveFrom });
        });

        model.Entity<ClientSettings>(b =>
        {
            b.ToTable("client_settings");
            // ClientId IS the primary key, not a separate surrogate — one row per client, so a
            // unique index on ClientId would just duplicate what the PK already gives for free.
            b.HasKey(s => s.ClientId);
            b.HasOne<Client>().WithMany().HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(s => s.ClientId == _tenantClientId);
        });

        model.Entity<PayRuleAssignmentEntity>(b =>
        {
            b.ToTable("pay_rule_assignments");
            b.HasKey(a => a.Id);
            // client_id leading, matching the plain-equality tenant filter below.
            b.HasIndex(a => new { a.ClientId, a.EmployeeId, a.EffectiveFrom });
            b.HasOne(a => a.PayRule).WithMany().HasForeignKey(a => a.PayRuleId);
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(a => a.ClientId == _tenantClientId);
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

            // Filtered on this entity's own ClientId column now, not Position.ClientId via
            // navigation — the Phase 1 switch the old comment here flagged as coming. Position stays
            // independently tenant-filtered too, so the two can never disagree in practice (an
            // assignment's ClientId and its Position's ClientId are set from the same client at
            // creation time and never re-parented across tenants).
            b.HasQueryFilter(a => a.ClientId == _tenantClientId);
        });

        model.Entity<StateMinimumWage>(b =>
        {
            b.ToTable("state_minimum_wages");
            b.HasKey(s => s.Id);
            b.Property(s => s.Amount).HasPrecision(19, 4);
            b.HasIndex(s => new { s.State, s.EffectiveFrom });
        });

        model.Entity<TimecardApproval>(b =>
        {
            b.ToTable("timecard_approvals");
            b.HasKey(a => a.Id);
            // "Is this employee/period locked right now" (TimecardLockService) and "what's the active
            // approval for this period" (TimecardService) both filter on all four and then pick the
            // row with UnapprovedAt still null — this index serves both without needing UnapprovedAt
            // in it, since there are only ever a handful of rows per (employee, period) to scan.
            b.HasIndex(a => new { a.ClientId, a.EmployeeId, a.PeriodStart, a.PeriodEnd });
            b.HasOne<Client>().WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(a => a.ClientId == _tenantClientId);
        });

        model.Entity<PunchImportBatch>(b =>
        {
            b.ToTable("punch_import_batches");
            b.HasKey(i => i.Id);
            b.HasIndex(i => new { i.ClientId, i.ImportedAt });
            b.HasOne<Client>().WithMany().HasForeignKey(i => i.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(i => i.ClientId == _tenantClientId);
        });

        model.Entity<PayrollExportProfile>(b =>
        {
            b.ToTable("payroll_export_profiles");
            b.HasKey(p => p.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter("Tenant", p => p.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", p => !p.IsDeleted);
        });

        model.Entity<PayrollEarningCodeMapping>(b =>
        {
            b.ToTable("payroll_earning_code_mappings");
            b.HasKey(m => m.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(m => m.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PayrollExportProfile>().WithMany().HasForeignKey(m => m.ProfileId).OnDelete(DeleteBehavior.Restrict);

            // Filtered on this entity's own ClientId, never through the ProfileId navigation — same
            // reasoning EmployeePositionAssignmentEntity's own comment gives: the two are set from the
            // same client at creation and never re-parented, so filtering the owned column directly
            // stays sargable without depending on a join.
            b.HasQueryFilter("Tenant", m => m.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", m => !m.IsDeleted);

            // The export's hot read: "every mapping for this profile."
            b.HasIndex(m => new { m.ClientId, m.ProfileId });

            // One earning code per line kind per profile. Partial on is_deleted=false — without the
            // filter, soft-deleting a mapping would permanently block recreating the same one.
            b.HasIndex(m => new { m.ClientId, m.ProfileId, m.LineType, m.LineCode })
                .IsUnique()
                .HasFilter("is_deleted = false");
        });

        model.Entity<PayrollEmployeeIdentifier>(b =>
        {
            b.ToTable("payroll_employee_identifiers");
            b.HasKey(i => i.Id);
            b.HasOne<Client>().WithMany().HasForeignKey(i => i.ClientId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PayrollExportProfile>().WithMany().HasForeignKey(i => i.ProfileId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Employee>().WithMany().HasForeignKey(i => i.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter("Tenant", i => i.ClientId == _tenantClientId);
            b.HasQueryFilter("SoftDelete", i => !i.IsDeleted);

            b.HasIndex(i => new { i.ClientId, i.ProfileId });

            // One identifier per employee per profile.
            b.HasIndex(i => new { i.ProfileId, i.EmployeeId })
                .IsUnique()
                .HasFilter("is_deleted = false");

            // The constraint that actually protects a paycheck: two RobTime employees can never map
            // to the same provider id within one profile, which would otherwise silently merge two
            // people's pay into one.
            b.HasIndex(i => new { i.ProfileId, i.ExternalEmployeeId })
                .IsUnique()
                .HasFilter("is_deleted = false");
        });
    }
}
