using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>Phase 6.7 — UI_PLAN.md decisions 21/23/24/25: approving a period locks its punches and
/// freezes its pay into a snapshot; a Pending PunchChangeRequest blocks approval; un-approving reopens
/// the period.</summary>
[Collection("Api")]
public class TimecardApprovalEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Approve_LocksThePeriod_AndFreezesGrossPay()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var approveResponse = await api.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await ReadTimecardAsync(approveResponse);
        Assert.True(approved!.IsLocked);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal($"test-client-admin-{clientId}", approved.ApprovedByUserId);
        var frozenGross = approved.GrossPay;
        Assert.True(frozenGross > 0);

        // The API itself refuses to write into a locked period (that's the other test below) — so to
        // actually prove the *read* path is frozen rather than merely "still correct because nothing
        // changed," write a second punch straight to the database, bypassing the lock check entirely.
        // If GET still reports the old total, it's reading the snapshot, not recalculating.
        await using (var db = CreateContext(clientId))
        {
            db.Punches.Add(new Punch
            {
                ClientId = clientId,
                EmployeeId = employeeId,
                PunchTime = InstantPattern.ExtendedIso.Parse("2026-06-03T13:00:00Z").Value,
                PunchTimeZoneId = "UTC",
                Kind = PunchKind.In,
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                CreatedBy = "test-direct-write",
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var getResponse = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var reread = await ReadTimecardAsync(getResponse);
        Assert.True(reread!.IsLocked);
        Assert.Equal(frozenGross, reread.GrossPay);
    }

    /// <summary>Buffers the full body before deserializing, rather than HttpContentJsonExtensions'
    /// streaming ReadFromJsonAsync — this endpoint's larger nested payload (week→day→shift→pair, per
    /// decision 25) intermittently made the streaming reader report required properties missing on a
    /// body that, read as a complete string first, parses cleanly. A test-client quirk, not a server
    /// bug: the raw response was verified complete and valid when this surfaced.</summary>
    private static async Task<TimecardResponse?> ReadTimecardAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<TimecardResponse>(body, TestJson.Options);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        await api.PostAsync($"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        var secondApprove = await api.PostAsync($"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondApprove.StatusCode);
    }

    [Fact]
    public async Task Approve_WithPendingChangeRequestInPeriod_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        var punchId = await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);

        await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "x", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);

        var approveResponse = await api.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);
    }

    [Fact]
    public async Task Approve_WithChangeRequestOutsidePeriod_Succeeds()
    {
        // Regression guard for the period-scoping in CountPendingRequestsInPeriodAsync: a pending
        // request against a *different* pay period must not block this one.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        // A punch (and a pending edit against it) far outside the biweekly period containing Jun 1.
        var farPunchId = await CreatePunchAsync(api, employeeId, "2026-09-01T13:00:00Z", PunchKind.In);
        await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = farPunchId, Reason = "x", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);

        var approveResponse = await api.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
    }

    [Fact]
    public async Task Approve_ThenDirectPunchMutations_AreBlocked()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        var punchId = await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var approveResponse = await api.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        // Create: a new punch landing inside the now-locked period.
        var createResponse = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = InstantPattern.ExtendedIso.Parse("2026-06-02T13:00:00Z").Value, Kind = PunchKind.In },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, createResponse.StatusCode);

        // Edit: the existing In punch, still inside the locked period.
        var editResponse = await api.PutAsJsonAsync(
            $"/punches/{punchId}",
            new UpdatePunchRequest { Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);

        // Delete: same punch.
        var deleteResponse = await api.DeleteAsync($"/punches/{punchId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        // Submitting a change request against it is a punch-mutating path too (UI_PLAN.md §8).
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, submitResponse.StatusCode);
    }

    [Fact]
    public async Task Unapprove_ReopensThePeriod_AndAllowsEditsAgain()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        var punchId = await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        await api.PostAsync($"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);

        var unapproveResponse = await api.PostAsync(
            $"/employees/{employeeId}/timecard/unapprove?date=2026-06-01", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, unapproveResponse.StatusCode);
        var unapproved = await unapproveResponse.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.False(unapproved!.IsLocked);

        var editResponse = await api.PutAsJsonAsync(
            $"/punches/{punchId}",
            new UpdatePunchRequest { Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
    }

    [Fact]
    public async Task Unapprove_NotCurrentlyApproved_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        var response = await api.PostAsync(
            $"/employees/{employeeId}/timecard/unapprove?date=2026-06-01", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApproveAndUnapprove_AsEmployee_Return403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var approveResponse = await employeeApi.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        var unapproveResponse = await employeeApi.PostAsync(
            $"/employees/{employeeId}/timecard/unapprove?date=2026-06-01", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, approveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unapproveResponse.StatusCode);
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    private static async Task<int> CreatePayRuleAsync(HttpClient api, int clientId, string name)
    {
        var response = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = name },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payRule = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return payRule!.Id;
    }

    private static async Task AssignPayRuleAsync(HttpClient api, int employeeId, int payRuleId, string effectiveFromIso)
    {
        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = payRuleId,
                EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse(effectiveFromIso)),
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> CreatePunchAsync(HttpClient api, int employeeId, string punchTimeIso, PunchKind kind)
    {
        var response = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = InstantPattern.ExtendedIso.Parse(punchTimeIso).Value, Kind = kind },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var punch = await response.Content.ReadFromJsonAsync<PunchResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return punch!.Id;
    }

    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
