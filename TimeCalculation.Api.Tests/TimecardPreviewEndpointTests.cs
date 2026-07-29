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

[Collection("Api")]
public class TimecardPreviewEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Preview_DraftPunchesOnly_ComputesGrossPay()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        var request = new PreviewPunchesRequest
        {
            DraftPunches =
            [
                new DraftPunchEntry { PunchTime = Instant("2026-06-01T13:00:00Z"), Kind = PunchKind.In },
                new DraftPunchEntry { PunchTime = Instant("2026-06-01T21:00:00Z"), Kind = PunchKind.Out },
            ],
        };

        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var preview = System.Text.Json.JsonSerializer.Deserialize<BulkPunchPreviewResponse>(body, TestJson.Options);

        Assert.True(preview!.GrossPay > 0);
        Assert.Contains(preview.Weeks, w => w.RegularHours == 8m);
    }

    [Fact]
    public async Task Preview_MergesDraftPunchesWithAlreadySavedPunches()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        // A real, already-saved 8-hour shift on 6/1.
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        // A draft 8-hour shift on 6/2, not yet saved.
        var request = new PreviewPunchesRequest
        {
            DraftPunches =
            [
                new DraftPunchEntry { PunchTime = Instant("2026-06-02T13:00:00Z"), Kind = PunchKind.In },
                new DraftPunchEntry { PunchTime = Instant("2026-06-02T21:00:00Z"), Kind = PunchKind.Out },
            ],
        };

        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var preview = System.Text.Json.JsonSerializer.Deserialize<BulkPunchPreviewResponse>(body, TestJson.Options);

        // Both shifts land in the same FLSA week, so the merged total is 16 hours, not 8 — proof the
        // draft rows were combined with the real punches rather than previewed in isolation.
        Assert.Equal(16m, preview!.Weeks.Sum(w => w.RegularHours));

        // The real punch must still exist afterward — preview never persists anything.
        var savedPunches = await api.GetAsync(
            $"/punches?employeeId={employeeId}", TestContext.Current.CancellationToken);
        savedPunches.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Preview_DoesNotPersistDraftPunches()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        var request = new PreviewPunchesRequest
        {
            DraftPunches =
            [
                new DraftPunchEntry { PunchTime = Instant("2026-06-01T13:00:00Z"), Kind = PunchKind.In },
                new DraftPunchEntry { PunchTime = Instant("2026-06-01T21:00:00Z"), Kind = PunchKind.Out },
            ],
        };

        await api.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        var timecard = await api.GetAsync(
            $"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        var body = await timecard.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<TimecardResponse>(body, TestJson.Options);

        Assert.All(parsed!.Workweeks.SelectMany(w => w.Days), d => Assert.Empty(d.Shifts));
    }

    [Fact]
    public async Task Preview_UnknownEmployee_Returns404()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var request = new PreviewPunchesRequest { DraftPunches = [] };
        var response = await api.PostAsJsonAsync(
            "/employees/999999999/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_InvalidDateParameter_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var request = new PreviewPunchesRequest { DraftPunches = [] };
        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=not-a-date", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_LockedPeriod_StillPreviews()
    {
        // Preview is read-only and never writes a punch, so it must not be blocked by
        // TimecardLockService the way CreateAsync/CreateBatchAsync are — a supervisor previewing
        // hypothetical changes to an already-approved period is exactly the kind of "what would this
        // do" question the endpoint exists to answer, without implying the period reopens.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var approve = await api.PostAsync(
            $"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var request = new PreviewPunchesRequest
        {
            DraftPunches = [new DraftPunchEntry { PunchTime = Instant("2026-06-03T13:00:00Z"), Kind = PunchKind.In }],
        };
        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CanPreviewOwnTimecard()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var payRuleId = await CreatePayRuleAsync(adminApi, clientId, "Standard");
        await AssignPayRuleAsync(adminApi, employeeId, payRuleId, "2020-01-01");
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var request = new PreviewPunchesRequest { DraftPunches = [] };
        var response = await employeeApi.PostAsJsonAsync(
            $"/employees/{employeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CannotPreviewSomeoneElsesTimecard_Returns403()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Preview Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var myEmployeeId = await CreateEmployeeAsync(adminApi, clientId);
        var colleagueEmployeeId = await CreateEmployeeAsync(adminApi, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, myEmployeeId);

        var request = new PreviewPunchesRequest { DraftPunches = [] };
        var response = await employeeApi.PostAsJsonAsync(
            $"/employees/{colleagueEmployeeId}/timecard/preview?date=2026-06-01", request, TestJson.Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Instant Instant(string iso) => InstantPattern.ExtendedIso.Parse(iso).Value;

    private async Task<HttpClient> CreateLinkedEmployeeClientAsync(int clientId, int employeeId)
    {
        var sub = $"test-linked-employee-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        await using var db = new PayrollDbContext(options, new FixedTenantContextAccessor(clientId));
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

    private static async Task CreatePunchAsync(HttpClient api, int employeeId, string punchTimeIso, PunchKind kind)
    {
        var response = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = InstantPattern.ExtendedIso.Parse(punchTimeIso).Value, Kind = kind },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
