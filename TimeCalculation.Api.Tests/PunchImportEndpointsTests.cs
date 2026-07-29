using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PunchImportEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Import_ValidCsv_CreatesPunchesAndBatch_AndWritesAuditEntries()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T13:00:00,In\n" +
                   $"{employeeId},2026-06-01T21:00:00,Out\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await ReadBatchAsync(response);
        Assert.Equal(2, batch!.PunchCount);
        Assert.Equal($"test-client-admin-{clientId}", batch.ImportedByUserId);
        Assert.Null(batch.DeletedAt);

        await using var db = CreateContext(clientId);
        var punches = await db.Punches.Where(p => p.EmployeeId == employeeId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, punches.Count);
        Assert.All(punches, p => Assert.Equal(batch.Id, p.ImportBatchId));

        var auditCount = await db.PunchAudits.CountAsync(
            a => punches.Select(p => p.Id).Contains(a.PunchId) && a.Action == "Created", TestContext.Current.CancellationToken);
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task Import_OneInvalidRow_ImportsNothing()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        // Row 1 valid, row 2 has an unknown employee id.
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T13:00:00,In\n" +
                   "999999999,2026-06-01T21:00:00,Out\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[2].EmployeeId", body);

        await using var db = CreateContext(clientId);
        var count = await db.Punches.CountAsync(p => p.EmployeeId == employeeId, TestContext.Current.CancellationToken);
        Assert.Equal(0, count); // the valid row 1 was NOT imported either — all-or-nothing
        Assert.Equal(0, await db.PunchImportBatches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Import_MissingRequiredColumn_ReturnsFileLevelError()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        // No PunchTime column.
        var csv = "EmployeeId,Kind\n1,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"file\"", body);
        Assert.Contains("PunchTime", body);
    }

    [Fact]
    public async Task Import_EmptyFile_Returns400()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_HeaderOnly_ReturnsFileLevelError()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsync("/punch-imports", BuildCsvContent("EmployeeId,PunchTime,Kind\n"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("no data rows", body);
    }

    [Fact]
    public async Task Import_FixedDollarWithoutAmount_ReturnsRowScopedError()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T13:00:00,FixedDollar\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].amount", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_InvalidPunchTime_ReturnsRowScopedError()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},not-a-date,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].PunchTime", body);
    }

    [Fact]
    public async Task Import_PunchTimeWithUtcOffset_ReturnsRowScopedError()
    {
        // A pre-resolved Instant string (what this endpoint accepted before the DST rework, and what
        // a client might still paste in by habit) must fail cleanly, not get silently misread as a
        // local time with the "Z"/offset ignored.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T13:00:00Z,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].PunchTime", body);
    }

    [Fact]
    public async Task Import_SpringForwardGap_ReturnsRowScopedError()
    {
        // 2026-03-08 is the US spring-forward date: America/New_York clocks jump from 01:59:59
        // straight to 03:00:00, so every local time from 02:00:00 to 02:59:59 never happens.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId); // default HomeTimeZoneId: America/New_York
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-03-08T02:30:00,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].PunchTime", body);
        Assert.Contains("does not exist", body);

        await using var db = CreateContext(clientId);
        Assert.Equal(0, await db.Punches.CountAsync(p => p.EmployeeId == employeeId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Import_FallBackAmbiguous_NoFlagGiven_ReturnsRowScopedError()
    {
        // 2026-11-01 is the US fall-back date: America/New_York clocks go from 01:59:59 back to
        // 01:00:00, so every local time from 01:00:00 to 01:59:59 happens twice. With no
        // DaylightSaving column at all, the row can't be resolved.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-11-01T01:30:00,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].DaylightSaving", body);

        await using var db = CreateContext(clientId);
        Assert.Equal(0, await db.Punches.CountAsync(p => p.EmployeeId == employeeId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Import_FallBackAmbiguous_InvalidFlagValue_ReturnsRowScopedError()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind,DaylightSaving\n" +
                   $"{employeeId},2026-11-01T01:30:00,In,maybe\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].DaylightSaving", body);
    }

    [Fact]
    public async Task Import_FallBackAmbiguous_DaylightSavingTrueVsFalse_ResolveOneHourApart()
    {
        // Same wall-clock local time, split across two separate imports by the DaylightSaving flag:
        // true picks the earlier (still-daylight-time) occurrence, false picks the later
        // (already-standard-time) one. They must land exactly one hour apart in UTC, and true must
        // be the earlier instant.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var daylightBatch = await ReadBatchAsync(await api.PostAsync(
            "/punch-imports",
            BuildCsvContent("EmployeeId,PunchTime,Kind,DaylightSaving\n" + $"{employeeId},2026-11-01T01:30:00,In,true\n"),
            TestContext.Current.CancellationToken));
        var standardBatch = await ReadBatchAsync(await api.PostAsync(
            "/punch-imports",
            BuildCsvContent("EmployeeId,PunchTime,Kind,DaylightSaving\n" + $"{employeeId},2026-11-01T01:30:00,In,false\n"),
            TestContext.Current.CancellationToken));

        await using var db = CreateContext(clientId);
        var daylightInstant = await db.Punches.Where(p => p.ImportBatchId == daylightBatch!.Id).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);
        var standardInstant = await db.Punches.Where(p => p.ImportBatchId == standardBatch!.Id).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Duration.FromHours(1), standardInstant - daylightInstant);
        Assert.True(daylightInstant < standardInstant);
    }

    [Fact]
    public async Task Import_SameLocalTime_ResolvesDifferentInstants_AcrossDstSeasons()
    {
        // The whole point of resolving against the real tzdb zone instead of a fixed offset: the
        // same wall-clock 13:00 local resolves to a different UTC instant in summer (EDT, UTC-4)
        // than in winter (EST, UTC-5) for the same employee/zone.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var summerBatch = await ReadBatchAsync(await api.PostAsync(
            "/punch-imports",
            BuildCsvContent("EmployeeId,PunchTime,Kind\n" + $"{employeeId},2026-06-01T13:00:00,In\n"),
            TestContext.Current.CancellationToken));
        var winterBatch = await ReadBatchAsync(await api.PostAsync(
            "/punch-imports",
            BuildCsvContent("EmployeeId,PunchTime,Kind\n" + $"{employeeId},2026-01-15T13:00:00,In\n"),
            TestContext.Current.CancellationToken));

        await using var db = CreateContext(clientId);
        var summerInstant = await db.Punches.Where(p => p.ImportBatchId == summerBatch!.Id).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);
        var winterInstant = await db.Punches.Where(p => p.ImportBatchId == winterBatch!.Id).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Instant.FromUtc(2026, 6, 1, 17, 0, 0), summerInstant);
        Assert.Equal(Instant.FromUtc(2026, 1, 15, 18, 0, 0), winterInstant);
    }

    [Fact]
    public async Task Import_NonDstZone_ResolvesWithoutDaylightSavingFlag()
    {
        // The same local time that's ambiguous in America/New_York isn't ambiguous at all in a zone
        // with no DST — the DaylightSaving column should never be needed there.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind,PunchTimeZoneId\n" +
                   $"{employeeId},2026-11-01T01:30:00,In,UTC\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = CreateContext(clientId);
        var instant = await db.Punches.Where(p => p.EmployeeId == employeeId).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Instant.FromUtc(2026, 11, 1, 1, 30, 0), instant);
    }

    [Fact]
    public async Task Import_PunchTimeZoneIdOmitted_DefaultsToEmployeeHomeZone()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId); // default HomeTimeZoneId: America/New_York (EST, UTC-5, in January)
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-01-15T08:00:00,In\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = CreateContext(clientId);
        var instant = await db.Punches.Where(p => p.EmployeeId == employeeId).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Instant.FromUtc(2026, 1, 15, 13, 0, 0), instant);
    }

    [Fact]
    public async Task Import_ExplicitPunchTimeZoneId_OverridesEmployeeHomeZone()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId); // default HomeTimeZoneId: America/New_York
        // Explicit column overrides to Los Angeles (PST, UTC-8, in January) — a different offset
        // than the employee's own home zone would give for the same local time.
        var csv = "EmployeeId,PunchTime,Kind,PunchTimeZoneId\n" +
                   $"{employeeId},2026-01-15T08:00:00,In,America/Los_Angeles\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = CreateContext(clientId);
        var instant = await db.Punches.Where(p => p.EmployeeId == employeeId).Select(p => p.PunchTime).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Instant.FromUtc(2026, 1, 15, 16, 0, 0), instant);
    }

    [Fact]
    public async Task Import_InvalidPunchTimeZoneId_ReturnsRowScopedError()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind,PunchTimeZoneId\n" +
                   $"{employeeId},2026-01-15T08:00:00,In,Not/AZone\n";

        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("row[1].PunchTimeZoneId", body);
    }

    [Fact]
    public async Task Import_TargetingLockedPeriod_Returns409_AndImportsNothing()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        // A real punch so the period has something to approve.
        await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = ParseInstant("2026-06-01T13:00:00Z"), Kind = PunchKind.In },
            TestJson.Options, TestContext.Current.CancellationToken);
        var approve = await api.PostAsync($"/employees/{employeeId}/timecard/approve?date=2026-06-01", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T21:00:00,Out\n";
        var response = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = CreateContext(clientId);
        Assert.Equal(0, await db.PunchImportBatches.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteBatch_HardDeletesPunches_KeepsAuditEntries_MarksBatchDeleted()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" +
                   $"{employeeId},2026-06-01T13:00:00,In\n" +
                   $"{employeeId},2026-06-01T21:00:00,Out\n";
        var importResponse = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);
        var batch = await ReadBatchAsync(importResponse);

        await using (var dbBefore = CreateContext(clientId))
        {
            Assert.Equal(2, await dbBefore.Punches.CountAsync(p => p.ImportBatchId == batch!.Id, TestContext.Current.CancellationToken));
        }

        var deleteResponse = await api.DeleteAsync($"/punch-imports/{batch!.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleted = await ReadBatchAsync(deleteResponse);
        Assert.NotNull(deleted!.DeletedAt);
        Assert.Equal($"test-client-admin-{clientId}", deleted.DeletedByUserId);

        await using var db = CreateContext(clientId);
        // Hard-deleted — gone even bypassing the soft-delete filter, not just IsDeleted = true.
        var remaining = await db.Punches.IgnoreQueryFilters()
            .Where(p => p.ImportBatchId == batch.Id).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, remaining);

        // Audit entries survive the punches they describe.
        var auditCount = await db.PunchAudits.CountAsync(a => a.Action == "Created", TestContext.Current.CancellationToken);
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task DeleteBatch_AlreadyDeleted_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var csv = "EmployeeId,PunchTime,Kind\n" + $"{employeeId},2026-06-01T13:00:00,In\n";
        var importResponse = await api.PostAsync("/punch-imports", BuildCsvContent(csv), TestContext.Current.CancellationToken);
        var batch = await ReadBatchAsync(importResponse);
        await api.DeleteAsync($"/punch-imports/{batch!.Id}", TestContext.Current.CancellationToken);

        var secondDelete = await api.DeleteAsync($"/punch-imports/{batch.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteBatch_UnknownId_Returns404()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.DeleteAsync("/punch-imports/999999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBatches_ReturnsMostRecentFirst()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var firstCsv = "EmployeeId,PunchTime,Kind\n" + $"{employeeId},2026-06-01T13:00:00,In\n";
        var secondCsv = "EmployeeId,PunchTime,Kind\n" + $"{employeeId},2026-06-02T13:00:00,In\n";
        var first = await ReadBatchAsync(await api.PostAsync("/punch-imports", BuildCsvContent(firstCsv), TestContext.Current.CancellationToken));
        var second = await ReadBatchAsync(await api.PostAsync("/punch-imports", BuildCsvContent(secondCsv), TestContext.Current.CancellationToken));

        var listResponse = await api.GetAsync("/punch-imports", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<PunchImportBatchResponse>>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.True(list!.Items.Count >= 2);
        var firstIndex = list.Items.ToList().FindIndex(b => b.Id == first!.Id);
        var secondIndex = list.Items.ToList().FindIndex(b => b.Id == second!.Id);
        Assert.True(secondIndex < firstIndex, "more recently imported batch should sort first");
    }

    [Fact]
    public async Task Supervisor_CannotImportPunches_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var supervisorApi = fixture.CreateAuthenticatedClient(AppRole.Supervisor, clientId, sub: $"test-supervisor-{Guid.NewGuid()}");

        var response = await supervisorApi.PostAsync(
            "/punch-imports", BuildCsvContent("EmployeeId,PunchTime,Kind\n1,2026-06-01T13:00:00,In\n"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_CannotDeleteImportBatch_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Import Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var supervisorApi = fixture.CreateAuthenticatedClient(AppRole.Supervisor, clientId, sub: $"test-supervisor-{Guid.NewGuid()}");

        var response = await supervisorApi.DeleteAsync("/punch-imports/1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Instant ParseInstant(string iso) => NodaTime.Text.InstantPattern.ExtendedIso.Parse(iso).Value;

    private static HttpContent BuildCsvContent(string csv, string fileName = "punches.csv")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static async Task<PunchImportBatchResponse?> ReadBatchAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<PunchImportBatchResponse>(body, TestJson.Options);
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

    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
