using Microsoft.EntityFrameworkCore;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Validates that the EF Core model builds against the Npgsql provider without a live database.
/// Constructing the model runs all the entity configuration and surfaces any mapping errors — the
/// most that can be verified in this environment (no PostgreSQL to migrate against).
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
    public void Punches_HaveEmployeePunchTimeIndex_AndUniqueDeviceIndex()
    {
        using var ctx = NewContext();
        var punch = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Punch))!;
        var indexes = punch.GetIndexes().ToList();

        Assert.Contains(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "EmployeeId", "PunchTime" }));
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
        Assert.NotNull(punch.GetQueryFilter());
    }

    [Fact]
    public void TenantFilter_AppliedWhenTenantSet()
    {
        using var ctx = NewContext(tenant: 42);
        var emp = ctx.Model.FindEntityType(typeof(TimeCalculation.Model.Employee))!;
        Assert.NotNull(emp.GetQueryFilter());
    }
}
