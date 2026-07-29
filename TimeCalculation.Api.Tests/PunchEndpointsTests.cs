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
public class PunchEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreatePunch_Succeeds_WritesAuditEntry()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var punch = await response.Content.ReadFromJsonAsync<PunchResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        // Direct DbContext, not another HTTP round-trip — there's no GET /punches/{id}/audit endpoint
        // (and doesn't need to be one just to prove the write happened); same ad-hoc-context pattern
        // TenantIsolationTests uses. Also re-fetches the punch itself rather than trusting the
        // HTTP-deserialized one for the CreatedAt comparison below — JSON round-trips an Instant at
        // millisecond precision, Postgres stores microseconds, so comparing against the client's copy
        // would fail on precision alone even though the server wrote the identical value to both rows.
        await using var db = CreateContext(clientId);
        var dbPunch = await db.Punches.SingleAsync(p => p.Id == punch!.Id, TestContext.Current.CancellationToken);
        var auditEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == punch!.Id, TestContext.Current.CancellationToken);

        Assert.Equal(clientId, auditEntry.ClientId);
        Assert.Equal("Created", auditEntry.Action);
        Assert.Equal($"test-client-admin-{clientId}", auditEntry.ActorUserId);
        Assert.Equal(dbPunch.CreatedAt, auditEntry.OccurredAt);
        Assert.Null(auditEntry.PreviousValues);
        Assert.Contains("\"kind\"", auditEntry.NewValues, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePunch_FixedDollarWithNoAmount_Returns400()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.FixedDollar,
        };

        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_AsEmployeeWithNoLinkedRecord_Returns403()
    {
        // Still 403, but as of Phase 6.4 for a different reason than when this test was written: the
        // route now accepts any authenticated role, and it's EmployeeScopeResolver that rejects this
        // caller — an Employee-role sub with no AppUser row linking it to an Employee has nothing to
        // punch *as*. An employee who IS linked can punch for themselves; see SelfServiceClockTests.
        var (clientId, _, employeeId) = await CreateEmployeeAsync();
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var response = await employeeApi.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_UnknownEmployee_Returns404()
    {
        var (_, api, _) = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = 999999999,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_DuplicateDeviceIdempotencyKey_Returns409OnSecondAttempt()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var deviceId = $"device-{Guid.NewGuid()}";
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
            DeviceId = deviceId,
            DevicePunchId = "abc123",
        };

        var first = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FullLifecycle_CreateListGetPutDelete_BehavesCorrectly()
    {
        var (clientId, api, employeeId) = await CreateEmployeeAsync();
        var punchTime = SystemClock.Instance.GetCurrentInstant();
        var createRequest = new CreatePunchRequest
        {
            EmployeeId = employeeId, PunchTime = punchTime, Kind = PunchKind.In,
        };
        var createResponse = await api.PostAsJsonAsync("/punches", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PunchResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        // Captured via a direct DB fetch, not the local `punchTime`/HTTP-deserialized `created.PunchTime`
        // — the request body itself already crossed one JSON boundary (client to server), which is
        // enough to introduce the same millisecond-vs-microsecond precision gap the other tests in this
        // file work around. Comparing this DB-sourced baseline against another DB fetch after the
        // update (below) keeps both sides of that specific assertion on equal precision.
        await using var setupDb = CreateContext(clientId);
        var punchTimeAfterCreate = (await setupDb.Punches.SingleAsync(p => p.Id == created.Id, TestContext.Current.CancellationToken)).PunchTime;

        var listResponse = await api.GetAsync($"/punches?employeeId={employeeId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<PunchResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(list!.Items, p => p.Id == created.Id);

        var getResponse = await api.GetAsync($"/punches/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var updateRequest = new UpdatePunchRequest { Kind = PunchKind.Out, Reason = "Corrected kind" };
        var putResponse = await api.PutAsJsonAsync($"/punches/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<PunchResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(PunchKind.Out, updated!.Kind);

        // PunchTime untouched by the update request above — partial-patch semantics
        // (UpdatePunchRequest's own doc comment): omitted fields keep the punch's existing value, not
        // PunchKind's default.
        await using var db = CreateContext(clientId);
        var dbPunchAfterUpdate = await db.Punches.SingleAsync(p => p.Id == created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(punchTimeAfterCreate, dbPunchAfterUpdate.PunchTime);

        var editEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == created.Id && a.Action == "Edited", TestContext.Current.CancellationToken);
        Assert.Equal("Corrected kind", editEntry.Reason);
        Assert.Contains("\"in\"", editEntry.PreviousValues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"out\"", editEntry.NewValues, StringComparison.OrdinalIgnoreCase);

        var deleteResponse = await api.DeleteAsync($"/punches/{created.Id}?reason=Duplicate+entry", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Soft-deleted, so both the direct GET and the list (PayrollDbContext's own SoftDelete query
        // filter) stop seeing it — same convention as every other entity's delete.
        var getAfterDelete = await api.GetAsync($"/punches/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
        var listAfterDelete = await api.GetAsync($"/punches?employeeId={employeeId}", TestContext.Current.CancellationToken);
        var listAfterDeleteBody = await listAfterDelete.Content.ReadFromJsonAsync<PagedResult<PunchResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(listAfterDeleteBody!.Items, p => p.Id == created.Id);

        var deleteEntry = await db.PunchAudits.SingleAsync(a => a.PunchId == created.Id && a.Action == "Deleted", TestContext.Current.CancellationToken);
        Assert.Equal("Duplicate entry", deleteEntry.Reason);
        Assert.Null(deleteEntry.NewValues);
    }

    [Fact]
    public async Task UpdatePunch_MergedResultInvalidCombination_Returns400()
    {
        // Existing punch is Kind=In (no Amount) — switching Kind to FixedDollar without also
        // supplying Amount produces an invalid merged punch that only PunchRequestValidator.
        // ValidateConsistency (run against the merge, not the raw request) catches.
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var createRequest = new CreatePunchRequest
        {
            EmployeeId = employeeId, PunchTime = SystemClock.Instance.GetCurrentInstant(), Kind = PunchKind.In,
        };
        var createResponse = await api.PostAsJsonAsync("/punches", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var created = (await createResponse.Content.ReadFromJsonAsync<PunchResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var updateRequest = new UpdatePunchRequest { Kind = PunchKind.FixedDollar };
        var putResponse = await api.PutAsJsonAsync($"/punches/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatePunch_UnknownId_Returns404()
    {
        var (_, api, _) = await CreateEmployeeAsync();
        var updateRequest = new UpdatePunchRequest { Kind = PunchKind.Out };

        var response = await api.PutAsJsonAsync("/punches/999999999", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePunch_UnknownId_Returns404()
    {
        var (_, api, _) = await CreateEmployeeAsync();

        var response = await api.DeleteAsync("/punches/999999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListPunches_InvalidFromParameter_Returns400()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();

        var response = await api.GetAsync($"/punches?employeeId={employeeId}&from=not-a-date", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(int ClientId, HttpClient Api, int EmployeeId)> CreateEmployeeAsync()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Punch Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var employeeRequest = new CreateEmployeeRequest
        {
            ClientId = clientId,
            FirstName = "Test",
            LastName = "Employee",
            MinimumWage = 15m,
        };
        var employeeResponse = await api.PostAsJsonAsync("/employees", employeeRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return (clientId, api, employee!.Id);
    }

    // See TenantIsolationTests' own CreateContext for why this bypasses the HTTP surface — same
    // reasoning applies here, just for one assertion rather than a whole isolation suite.
    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
