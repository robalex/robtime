using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PunchChangeRequestEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task AddRequest_Approved_CreatesPunchAndAuditEntry_AndBackfillsPunchId()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Add,
            EmployeeId = employeeId,
            Reason = "Missed clock-in",
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var submitResponse = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Null(submitted.PunchId);
        Assert.Equal(PunchChangeRequestStatus.Pending, submitted.Status);

        var decideRequest = new DecidePunchChangeRequestRequest { Approve = true, ReviewNote = "Looks right" };
        var decideResponse = await api.PostAsJsonAsync($"/punch-change-requests/{submitted.Id}/decide", decideRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);
        var decided = (await decideResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(PunchChangeRequestStatus.Approved, decided.Status);
        Assert.NotNull(decided.PunchId);

        await using var db = CreateContext(clientId);
        var punch = await db.Punches.SingleAsync(p => p.Id == decided.PunchId!.Value, TestContext.Current.CancellationToken);
        Assert.Equal(employeeId, punch.EmployeeId);
        Assert.Equal(PunchKind.In, punch.Kind);

        var auditEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == punch.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Created", auditEntry.Action);
        // Actor is the requester, not the reviewer — PunchChangeRequest's own doc comment on why.
        Assert.Equal($"test-client-admin-{clientId}", auditEntry.ActorUserId);
    }

    [Fact]
    public async Task EditRequest_Approved_UpdatesPunchAndWritesAuditEntry()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);

        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Edit,
            PunchId = punchId,
            Reason = "Wrong kind entered",
            Kind = PunchKind.Out,
        };
        var submitResponse = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var decideResponse = await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide",
            new DecidePunchChangeRequestRequest { Approve = true },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);

        await using var db = CreateContext(clientId);
        var punch = await db.Punches.SingleAsync(p => p.Id == punchId, TestContext.Current.CancellationToken);
        Assert.Equal(PunchKind.Out, punch.Kind);

        var auditEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == punchId && a.Action == "Edited", TestContext.Current.CancellationToken);
        Assert.Contains("\"in\"", auditEntry.PreviousValues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"out\"", auditEntry.NewValues, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Wrong kind entered", auditEntry.Reason);
    }

    [Fact]
    public async Task DeleteRequest_Approved_SoftDeletesPunchAndWritesAuditEntry()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);

        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "Duplicate punch",
        };
        var submitResponse = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide",
            new DecidePunchChangeRequestRequest { Approve = true },
            TestJson.Options, TestContext.Current.CancellationToken);

        var getAfterApproval = await api.GetAsync($"/punches/{punchId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterApproval.StatusCode);

        await using var db = CreateContext(clientId);
        var auditEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == punchId && a.Action == "Deleted", TestContext.Current.CancellationToken);
        Assert.Null(auditEntry.NewValues);
    }

    [Fact]
    public async Task Denied_WritesNoAuditEntry_AndLeavesPunchUnchanged()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);

        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "Requesting change", Kind = PunchKind.Out,
        };
        var submitResponse = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var decideResponse = await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide",
            new DecidePunchChangeRequestRequest { Approve = false, ReviewNote = "Not enough context" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var decided = (await decideResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(PunchChangeRequestStatus.Denied, decided.Status);

        await using var db = CreateContext(clientId);
        var punch = await db.Punches.SingleAsync(p => p.Id == punchId, TestContext.Current.CancellationToken);
        Assert.Equal(PunchKind.In, punch.Kind); // unchanged

        var auditCount = await db.PunchAudits.CountAsync(a => a.PunchId == punchId, TestContext.Current.CancellationToken);
        Assert.Equal(1, auditCount); // just the original "Created" entry from CreatePunchAsync — no "Edited"
    }

    [Fact]
    public async Task Decide_AlreadyDecided_Returns409()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide", new DecidePunchChangeRequestRequest { Approve = true },
            TestJson.Options, TestContext.Current.CancellationToken);

        var secondDecide = await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide", new DecidePunchChangeRequestRequest { Approve = false },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondDecide.StatusCode);
    }

    [Fact]
    public async Task AddRequest_MissingEmployeeId_Returns400()
    {
        var (_, api, _) = await CreateEmployeeAsync();
        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Add,
            Reason = "Missed punch",
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var response = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditRequest_UnknownPunchId_Returns404()
    {
        var (_, api, _) = await CreateEmployeeAsync();
        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Edit, PunchId = 999999999, Reason = "x", Kind = PunchKind.Out,
        };

        var response = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditRequest_MergedResultInvalid_RejectedAtDecideTime()
    {
        // The FixedDollar/Amount check can't run at submission for an Edit (no existing punch to
        // merge against yet — PunchChangeRequestValidator's own doc comment) — this proves it's
        // actually enforced later, at decide time, not silently skipped altogether.
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var submitRequest = new SubmitPunchChangeRequestRequest
        {
            ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "x", Kind = PunchKind.FixedDollar,
        };
        var submitResponse = await api.PostAsJsonAsync("/punch-change-requests", submitRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode); // accepted at submit time
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var decideResponse = await api.PostAsJsonAsync(
            $"/punch-change-requests/{submitted.Id}/decide", new DecidePunchChangeRequestRequest { Approve = true },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, decideResponse.StatusCode);
    }

    [Fact]
    public async Task ListPunchChangeRequests_FiltersByStatus()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var pendingResponse = await api.GetAsync($"/punch-change-requests?status=Pending&employeeId={employeeId}", TestContext.Current.CancellationToken);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<PagedResult<PunchChangeRequestResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(pending!.Items, r => r.Id == submitted.Id);

        var approvedResponse = await api.GetAsync($"/punch-change-requests?status=Approved&employeeId={employeeId}", TestContext.Current.CancellationToken);
        var approved = await approvedResponse.Content.ReadFromJsonAsync<PagedResult<PunchChangeRequestResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(approved!.Items, r => r.Id == submitted.Id);
    }

    [Fact]
    public async Task GetPunchChangeRequest_EnrichesEmployeeNameAndCurrentPunch()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "x", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        // Submit's own response shouldn't pay for the enrichment lookup — only List/Get do.
        Assert.Null(submitted.EmployeeFirstName);
        Assert.Null(submitted.CurrentPunch);

        var getResponse = await api.GetAsync($"/punch-change-requests/{submitted.Id}", TestContext.Current.CancellationToken);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal("Test", fetched!.EmployeeFirstName);
        Assert.Equal("Employee", fetched.EmployeeLastName);
        Assert.NotNull(fetched.CurrentPunch);
        Assert.Equal(punchId, fetched.CurrentPunch!.Id);
        Assert.Equal(PunchKind.In, fetched.CurrentPunch.Kind); // the punch as it is today, not the requested Out
    }

    [Fact]
    public async Task ListPunchChangeRequests_EnrichesEmployeeNameAndCurrentPunch_ForEveryItem()
    {
        // Batch-enrichment path (EnrichAsync via ListAsync), not the single-item Get path — proves
        // the employee/punch dictionaries built for the page actually get applied to every row.
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);

        var listResponse = await api.GetAsync($"/punch-change-requests?status=Pending&employeeId={employeeId}", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<PunchChangeRequestResponse>>(TestJson.Options, TestContext.Current.CancellationToken);

        var item = Assert.Single(list!.Items);
        Assert.Equal("Test", item.EmployeeFirstName);
        Assert.NotNull(item.CurrentPunch);
    }

    [Fact]
    public async Task AddRequest_HasNoCurrentPunch_EmployeeNameStillResolved()
    {
        // Add has nothing to compare against yet (no target punch exists until approval creates
        // one) — CurrentPunch should stay null rather than erroring, while the employee name (looked
        // up directly, not via a punch) still resolves.
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest
            {
                ChangeKind = PunchChangeKind.Add, EmployeeId = employeeId, Reason = "Missed clock-in",
                PunchTime = SystemClock.Instance.GetCurrentInstant(), Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var getResponse = await api.GetAsync($"/punch-change-requests/{submitted.Id}", TestContext.Current.CancellationToken);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal("Test", fetched!.EmployeeFirstName);
        Assert.Null(fetched.CurrentPunch);
    }

    [Fact]
    public async Task Employee_CanSubmitEditRequestForOwnPunch()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "Wrong time", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submitted = await response.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(employeeId, submitted!.EmployeeId);
    }

    [Fact]
    public async Task Employee_CanSubmitAddRequestForSelf()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest
            {
                ChangeKind = PunchChangeKind.Add, EmployeeId = employeeId, Reason = "Missed clock-in",
                PunchTime = SystemClock.Instance.GetCurrentInstant(), Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CannotSubmitEditRequestForColleaguesPunch_Returns403()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var colleagueId = await CreateColleagueAsync(api, clientId);
        var colleaguePunchId = await CreatePunchAsync(api, colleagueId, PunchKind.In);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = colleaguePunchId, Reason = "x", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CannotSubmitAddRequestForColleague_Returns403()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var colleagueId = await CreateColleagueAsync(api, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest
            {
                ChangeKind = PunchChangeKind.Add, EmployeeId = colleagueId, Reason = "x",
                PunchTime = SystemClock.Instance.GetCurrentInstant(), Kind = PunchKind.In,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CanListAndGetOwnRequest()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);
        var submitResponse = await employeeApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = punchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        // No employeeId query param — an Employee caller sees their own regardless.
        var listResponse = await employeeApi.GetAsync("/punch-change-requests", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<PunchChangeRequestResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(list!.Items, r => r.Id == submitted.Id);

        var getResponse = await employeeApi.GetAsync($"/punch-change-requests/{submitted.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Employee_ListDoesNotLeakColleaguesRequests()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var colleagueId = await CreateColleagueAsync(api, clientId);
        var colleaguePunchId = await CreatePunchAsync(api, colleagueId, PunchKind.In);
        await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = colleaguePunchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        // Explicitly asking for the colleague's requests doesn't work either — the caller's own id
        // wins over whatever employeeId filter was passed, it isn't just a default.
        var response = await employeeApi.GetAsync($"/punch-change-requests?employeeId={colleagueId}", TestContext.Current.CancellationToken);

        var list = await response.Content.ReadFromJsonAsync<PagedResult<PunchChangeRequestResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Employee_CannotGetColleaguesRequestById_Returns403()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var colleagueId = await CreateColleagueAsync(api, clientId);
        var colleaguePunchId = await CreatePunchAsync(api, colleagueId, PunchKind.In);
        var submitResponse = await api.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Delete, PunchId = colleaguePunchId, Reason = "x" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var submitted = (await submitResponse.Content.ReadFromJsonAsync<PunchChangeRequestResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.GetAsync($"/punch-change-requests/{submitted.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EmployeeWithNoLinkedRecord_CannotSubmitListOrGet_Returns403()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchId = await CreatePunchAsync(api, employeeId, PunchKind.In);
        var unlinkedApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-unlinked-{Guid.NewGuid()}");

        var submitResponse = await unlinkedApi.PostAsJsonAsync(
            "/punch-change-requests",
            new SubmitPunchChangeRequestRequest { ChangeKind = PunchChangeKind.Edit, PunchId = punchId, Reason = "x", Kind = PunchKind.Out },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, submitResponse.StatusCode);

        var listResponse = await unlinkedApi.GetAsync("/punch-change-requests", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var getResponse = await unlinkedApi.GetAsync("/punch-change-requests/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);
    }

    private async Task<int> CreateColleagueAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Colleague", LastName = "Employee", MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    /// <summary>An Employee-role client whose sub has a real AppUser row linking it to
    /// <paramref name="employeeId"/> — same pattern as TimecardEndpointsTests/SelfServiceClockTests,
    /// duplicated here because these are the scoping rules for a different route.</summary>
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

    private async Task<(int ClientId, HttpClient Api, int EmployeeId)> CreateEmployeeAsync()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Punch Change Request Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var employeeRequest = new CreateEmployeeRequest
        {
            ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m,
        };
        var employeeResponse = await api.PostAsJsonAsync("/employees", employeeRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return (clientId, api, employee!.Id);
    }

    private static async Task<int> CreatePunchAsync(HttpClient api, int employeeId, PunchKind kind)
    {
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId, PunchTime = SystemClock.Instance.GetCurrentInstant(), Kind = kind,
        };
        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
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
