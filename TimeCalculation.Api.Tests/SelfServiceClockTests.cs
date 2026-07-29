using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Phase 6.4's self-service scoping. The security-critical assertions here are the negative ones:
/// an Employee must not be able to punch as anyone but themselves, and an account with no linked
/// Employee row must not be able to punch at all.
/// </summary>
[Collection("Api")]
public class SelfServiceClockTests(ApiFixture fixture)
{
    [Fact]
    public async Task Employee_CanPunchForThemselves()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest
            {
                EmployeeId = employeeId,
                PunchTime = SystemClock.Instance.GetCurrentInstant(),
                Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CannotPunchForSomeoneElse_Returns403()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var myEmployeeId = await CreateEmployeeAsync(adminApi, clientId);
        var colleagueEmployeeId = await CreateEmployeeAsync(adminApi, clientId, lastName: "Colleague");
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, myEmployeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest
            {
                EmployeeId = colleagueEmployeeId,
                PunchTime = SystemClock.Instance.GetCurrentInstant(),
                Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And nothing was written for the colleague — the rejection happens before any DB write,
        // so this proves the guard isn't just shaping the response after the fact.
        await using var db = CreateContext(clientId);
        var colleaguePunches = await db.Punches.CountAsync(
            p => p.EmployeeId == colleagueEmployeeId, TestContext.Current.CancellationToken);
        Assert.Equal(0, colleaguePunches);
    }

    [Fact]
    public async Task EmployeeWithNoLinkedRecord_CannotPunch_Returns403()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        // Employee role, but no AppUser row linking this sub to any Employee — the case every
        // ClientAdmin/Supervisor account is in by default.
        var unlinkedApi = fixture.CreateAuthenticatedClient(
            AppRole.Employee, clientId, sub: $"test-unlinked-{Guid.NewGuid()}");

        var response = await unlinkedApi.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest
            {
                EmployeeId = employeeId,
                PunchTime = SystemClock.Instance.GetCurrentInstant(),
                Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_CanStillPunchForAnyEmployee()
    {
        // Regression guard: opening POST /punches to the Employee policy must not narrow what a
        // Supervisor could already do.
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var supervisorApi = fixture.CreateAuthenticatedClient(
            AppRole.Supervisor, clientId, sub: $"test-supervisor-{Guid.NewGuid()}");

        var response = await supervisorApi.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest
            {
                EmployeeId = employeeId,
                PunchTime = SystemClock.Instance.GetCurrentInstant(),
                Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ClockStatus_ReflectsInThenOut()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var beforeAnyPunch = await employeeApi.GetFromJsonAsync<ClockStatusResponse>(
            "/me/clock-status", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.False(beforeAnyPunch!.IsClockedIn);
        Assert.Null(beforeAnyPunch.Since);
        Assert.Equal(employeeId, beforeAnyPunch.EmployeeId);

        var clockInAt = SystemClock.Instance.GetCurrentInstant();
        await PunchAsync(employeeApi, employeeId, clockInAt, PunchKind.In);

        var afterIn = await employeeApi.GetFromJsonAsync<ClockStatusResponse>(
            "/me/clock-status", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.True(afterIn!.IsClockedIn);
        Assert.NotNull(afterIn.Since);
        Assert.NotNull(afterIn.SincePunchId);

        await PunchAsync(employeeApi, employeeId, clockInAt.Plus(Duration.FromHours(4)), PunchKind.Out);

        var afterOut = await employeeApi.GetFromJsonAsync<ClockStatusResponse>(
            "/me/clock-status", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.False(afterOut!.IsClockedIn);
        Assert.Null(afterOut.Since);
    }

    [Fact]
    public async Task ClockStatus_UsesLatestPunchNotInsertionOrder()
    {
        // An Out backdated *before* the In must leave them clocked in — the rule is "latest by
        // PunchTime," not "last row written," which is exactly what a corrected/late-entered punch
        // produces.
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var now = SystemClock.Instance.GetCurrentInstant();
        await PunchAsync(employeeApi, employeeId, now, PunchKind.In);
        await PunchAsync(employeeApi, employeeId, now.Minus(Duration.FromHours(6)), PunchKind.Out);

        var status = await employeeApi.GetFromJsonAsync<ClockStatusResponse>(
            "/me/clock-status", TestJson.Options, TestContext.Current.CancellationToken);

        Assert.True(status!.IsClockedIn);
    }

    [Fact]
    public async Task ClockStatus_NoLinkedEmployeeRecord_Returns404()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync(
            $"Clock Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var unlinkedApi = fixture.CreateAuthenticatedClient(
            AppRole.Supervisor, clientId, sub: $"test-unlinked-{Guid.NewGuid()}");

        var response = await unlinkedApi.GetAsync("/me/clock-status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task PunchAsync(HttpClient api, int employeeId, Instant at, PunchKind kind)
    {
        var response = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = at, Kind = kind },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId, string lastName = "Employee")
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = lastName, MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    /// <summary>
    /// An Employee-role client whose sub has a real AppUser row pointing at <paramref name="employeeId"/>
    /// — the state POST /users produces for a self-service employee. Written directly rather than
    /// through that endpoint because these tests are about the scoping rules, not about provisioning.
    /// </summary>
    private async Task<HttpClient> CreateLinkedEmployeeClientAsync(int clientId, int employeeId)
    {
        var sub = $"test-linked-employee-{Guid.NewGuid()}";
        await using var db = CreateContext(clientId);
        db.AppUsers.Add(new AppUser
        {
            CognitoSub = sub,
            ClientId = clientId,
            EmployeeId = employeeId,
            DisplayName = "Test Employee",
            Role = AppRole.Employee,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub);
    }

    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
