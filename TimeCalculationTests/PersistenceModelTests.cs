using Microsoft.EntityFrameworkCore;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Validates that the EF Core model builds against the Npgsql provider without a live database.
/// Constructing the model runs all the entity configuration and surfaces any mapping errors, and
/// pins the schema decisions (precision, indexes, filters, foreign keys, snake_case naming) that
/// the generated migration depends on.
/// </summary>
public class PersistenceModelTests
{
    private static PayrollDbContext NewContext(int? tenant = null)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql("Host=localhost;Database=payroll;Username=x;Password=y", o => o.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, tenant);
    }

    [Fact]
    public void Model_Builds_WithoutMappingErrors()
    {
        using var ctx = NewContext();
        var model = ctx.Model;   // forces model construction
        Assert.NotNull(model);
    }

    [Fact]
    public void Punches_HaveClientIdLeadingPunchTimeIndex_AndUniqueDeviceIndex()
    {
        // client_id leads the hot index (tenancy schema prep) — a tenant filter that isn't the
        // leading index column isn't sargable under a generic query plan, so this has to be true
        // before the filter predicate itself ever lands (that's Phase 1).
        using var ctx = NewContext();
        var punch = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Punch))!;
        var indexes = punch.GetIndexes().ToList();

        Assert.Contains(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ClientId", "EmployeeId", "PunchTime" }));
        Assert.Contains(indexes, i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "EmployeeId", "DeviceId", "DevicePunchId" }));
    }

    [Fact]
    public void MoneyProperties_UseNineteenFourPrecision()
    {
        using var ctx = NewContext();
        var emp = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Employee))!;
        var wage = emp.FindProperty(nameof(TimeCalculation.Model.Employee.MinimumWage))!;
        Assert.Equal(19, wage.GetPrecision());
        Assert.Equal(4, wage.GetScale());
    }

    [Fact]
    public void SoftDeleteFilter_IsAppliedToPunches()
    {
        using var ctx = NewContext();
        var punch = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Punch))!;
        Assert.NotEmpty(punch.GetDeclaredQueryFilters());
    }

    [Fact]
    public void TenantFilter_AppliedWhenTenantSet()
    {
        using var ctx = NewContext(tenant: 42);
        var emp = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Employee))!;
        Assert.NotEmpty(emp.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Columns_UseSnakeCaseNaming()
    {
        // Tables were always snake_cased; the convention makes columns match so hand-written SQL
        // never needs quoting (`SELECT punch_time FROM punches`).
        using var ctx = NewContext();
        var punch = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Punch))!;

        Assert.Equal("punch_time", punch.FindProperty(nameof(TimeCalculation.Model.Punch.PunchTime))!.GetColumnName());
        Assert.Equal("device_punch_id", punch.FindProperty(nameof(TimeCalculation.Model.Punch.DevicePunchId))!.GetColumnName());
    }

    [Theory]
    [InlineData(typeof(TimeCalculation.Model.Employee), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.Position), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.PayRules.PayRule), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.Punch), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.PunchAuditEntry), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.DifferentialRule), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.HolidayCalendar), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.Premiums.ClientPremiumPolicy), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(PayRuleAssignmentEntity), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(PayRuleAssignmentEntity), "EmployeeId", typeof(TimeCalculation.Model.Employee))]
    [InlineData(typeof(EmployeePositionAssignmentEntity), "ClientId", typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(EmployeePositionAssignmentEntity), "EmployeeId", typeof(TimeCalculation.Model.Employee))]
    public void OwnershipColumns_HaveEnforcedForeignKeys(Type dependent, string fkProperty, Type principal)
    {
        // These were plain int columns with no constraint, so nothing stopped an orphaned row.
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(dependent)!;

        var fk = entity.GetForeignKeys().SingleOrDefault(f =>
            f.Properties.Count == 1
            && f.Properties[0].Name == fkProperty
            && f.PrincipalEntityType.ClrType == principal);

        Assert.NotNull(fk);
        // Restrict, not Cascade: deleting a client or employee must never silently delete payroll records.
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void PayRule_HasRuleFamilyIdIndex()
    {
        // Version-history lookup: "every version of this rule family" (Gap F — versioning never
        // mutates an Active row in place).
        using var ctx = NewContext();
        var payRule = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.PayRules.PayRule))!;

        Assert.Contains(payRule.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "RuleFamilyId" }));
    }

    [Fact]
    public void ClientPremiumPolicy_HasResolutionIndex_AndTenantFilter()
    {
        using var ctx = NewContext();
        var policy = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Premiums.ClientPremiumPolicy))!;

        Assert.Contains(policy.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ClientId", "PremiumCode", "EffectiveFrom" }));
        Assert.NotEmpty(policy.GetDeclaredQueryFilters());
    }

    [Theory]
    [InlineData(typeof(TimeCalculation.Model.DifferentialRule))]
    [InlineData(typeof(TimeCalculation.Model.HolidayCalendar))]
    public void NewTenantScopedEntities_HaveQueryFilter(Type entityType)
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(entityType)!;
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
    }

    [Theory]
    [InlineData(typeof(TimeCalculation.Model.Client))]
    [InlineData(typeof(TimeCalculation.Model.Employee))]
    [InlineData(typeof(TimeCalculation.Model.Position))]
    [InlineData(typeof(TimeCalculation.Model.PayRules.PayRule))]
    public void TenantAndSoftDeleteFilters_BothApply_NotOneOverwritingTheOther(Type entityType)
    {
        // EF Core 10's named filters are supposed to AND together rather than the second
        // HasQueryFilter call silently replacing the first (the old, single-filter-per-entity
        // behavior). Verified directly rather than trusted from the docs: both "Tenant" and
        // "SoftDelete" must be present as two distinct declared filters.
        using var ctx = NewContext(tenant: 42);
        var entity = ctx.Model.FindEntityType(entityType)!;
        var filters = entity.GetDeclaredQueryFilters();

        Assert.Equal(2, filters.Count);
        var keys = filters.Select(f => f.Key).ToList();
        Assert.Contains("Tenant", keys);
        Assert.Contains("SoftDelete", keys);
    }
}
