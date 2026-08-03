using System.Net;
using System.Net.Http.Json;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>Slice 3 — the first test in the repo to exercise the full vertical slice: punches →
/// engine → timecard approval → frozen snapshot → payroll export projection → CSV file.</summary>
[Collection("Api")]
public class PayrollExportBatchEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task FullLifecycle_CreateListDownload_ProducesExpectedCsv()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var timecard = await ApproveAsync(api, employeeId, "2026-06-01");

        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        await MapEarningCodeAsync(api, profileId, PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        await MapIdentifierAsync(api, profileId, employeeId, "EXT-1");

        var createResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest { PeriodStart = timecard.PeriodStart, PeriodEnd = timecard.PeriodEnd },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var batch = (await createResponse.Content.ReadFromJsonAsync<PayrollExportBatchResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(1, batch.EmployeeCount);
        Assert.True(batch.RowCount > 0);
        Assert.True(batch.TotalAmount > 0);
        Assert.Null(batch.VoidedAt);

        var listResponse = await api.GetAsync($"/payroll-export-profiles/{profileId}/exports", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<PayrollExportBatchResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(list!.Items, b => b.Id == batch.Id);

        var downloadResponse = await api.GetAsync(
            $"/payroll-export-profiles/{profileId}/exports/{batch.Id}/download", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var csv = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("EmployeeId,ExternalEmployeeId,EarningCode,WorkDate,Hours,Amount", csv);
        Assert.Contains($"{employeeId},EXT-1,REG", csv);
    }

    [Fact]
    public async Task CreateExport_NoApprovedTimecardsForPeriod_ReturnsValidationProblem()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest
            {
                PeriodStart = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")),
                PeriodEnd = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-14")),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExport_UnmappedEarningCode_ReturnsConflict()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);
        var timecard = await ApproveAsync(api, employeeId, "2026-06-01");

        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        // No earning-code mapping for Regular/"" configured at all — every line is unmapped.
        await MapIdentifierAsync(api, profileId, employeeId, "EXT-1");

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest { PeriodStart = timecard.PeriodStart, PeriodEnd = timecard.PeriodEnd },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateExport_EmployeeMissingIdentifier_ReturnsConflict()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);
        var timecard = await ApproveAsync(api, employeeId, "2026-06-01");

        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        await MapEarningCodeAsync(api, profileId, PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        // No PayrollEmployeeIdentifier configured for this employee on this profile.

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest { PeriodStart = timecard.PeriodStart, PeriodEnd = timecard.PeriodEnd },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateExport_SamePeriodTwiceWithoutVoiding_ReturnsConflict_ThenSucceedsAfterVoid()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);
        var timecard = await ApproveAsync(api, employeeId, "2026-06-01");

        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        await MapEarningCodeAsync(api, profileId, PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        await MapIdentifierAsync(api, profileId, employeeId, "EXT-1");

        var request = new CreatePayrollExportRequest { PeriodStart = timecard.PeriodStart, PeriodEnd = timecard.PeriodEnd };
        var firstResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBatch = (await firstResponse.Content.ReadFromJsonAsync<PayrollExportBatchResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        var secondResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var voidResponse = await api.PostAsync(
            $"/payroll-export-profiles/{profileId}/exports/{firstBatch.Id}/void", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        var voided = await voidResponse.Content.ReadFromJsonAsync<PayrollExportBatchResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(voided!.VoidedAt);

        var thirdResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, thirdResponse.StatusCode);
        var thirdBatch = await thirdResponse.Content.ReadFromJsonAsync<PayrollExportBatchResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotEqual(firstBatch.Id, thirdBatch!.Id);
    }

    [Fact]
    public async Task VoidExport_AlreadyVoided_ReturnsConflict()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);
        var timecard = await ApproveAsync(api, employeeId, "2026-06-01");

        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        await MapEarningCodeAsync(api, profileId, PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        await MapIdentifierAsync(api, profileId, employeeId, "EXT-1");
        var batch = await CreateExportAsync(api, profileId, timecard.PeriodStart, timecard.PeriodEnd);

        await api.PostAsync($"/payroll-export-profiles/{profileId}/exports/{batch.Id}/void", null, TestContext.Current.CancellationToken);
        var secondVoid = await api.PostAsync($"/payroll-export-profiles/{profileId}/exports/{batch.Id}/void", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondVoid.StatusCode);
    }

    [Fact]
    public async Task VoidExport_UnknownId_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var response = await api.PostAsync($"/payroll-export-profiles/{profileId}/exports/999999999/void", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AllFourEndpoints_AsEmployee_Return403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var createResponse = await employeeApi.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest
            {
                PeriodStart = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")),
                PeriodEnd = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-14")),
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        var listResponse = await employeeApi.GetAsync($"/payroll-export-profiles/{profileId}/exports", TestContext.Current.CancellationToken);
        var downloadResponse = await employeeApi.GetAsync($"/payroll-export-profiles/{profileId}/exports/1/download", TestContext.Current.CancellationToken);
        var voidResponse = await employeeApi.PostAsync($"/payroll-export-profiles/{profileId}/exports/1/void", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, voidResponse.StatusCode);
    }

    [Fact]
    public async Task DownloadAndVoid_AnotherClientsBatch_Return404()
    {
        var (clientAId, apiA) = await fixture.CreateClientAndScopedClientAsync($"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeAId = await CreateEmployeeAsync(apiA, clientAId);
        var payRuleAId = await CreatePayRuleAsync(apiA, clientAId, "Standard");
        await AssignPayRuleAsync(apiA, employeeAId, payRuleAId, "2020-01-01");
        await CreatePunchAsync(apiA, employeeAId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(apiA, employeeAId, "2026-06-01T21:00:00Z", PunchKind.Out);
        var timecardA = await ApproveAsync(apiA, employeeAId, "2026-06-01");
        var profileAId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(apiA, clientAId);
        await MapEarningCodeAsync(apiA, profileAId, PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        await MapIdentifierAsync(apiA, profileAId, employeeAId, "EXT-1");
        var batchA = await CreateExportAsync(apiA, profileAId, timecardA.PeriodStart, timecardA.PeriodEnd);

        var (_, apiB) = await fixture.CreateClientAndScopedClientAsync($"Export Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var downloadResponse = await apiB.GetAsync(
            $"/payroll-export-profiles/{profileAId}/exports/{batchA.Id}/download", TestContext.Current.CancellationToken);
        var voidResponse = await apiB.PostAsync(
            $"/payroll-export-profiles/{profileAId}/exports/{batchA.Id}/void", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, voidResponse.StatusCode);
    }

    private static async Task<PayrollExportBatchResponse> CreateExportAsync(
        HttpClient api, int profileId, LocalDate periodStart, LocalDate periodEnd)
    {
        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/exports",
            new CreatePayrollExportRequest { PeriodStart = periodStart, PeriodEnd = periodEnd },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayrollExportBatchResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;
    }

    private static async Task MapEarningCodeAsync(
        HttpClient api, int profileId, PayLineType lineType, string lineCode, string earningCode, PayrollExportValueBasis valueBasis)
    {
        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = lineType, LineCode = lineCode, EarningCode = earningCode, ValueBasis = valueBasis,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task MapIdentifierAsync(HttpClient api, int profileId, int employeeId, string externalEmployeeId)
    {
        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = externalEmployeeId },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<TimecardResponse> ApproveAsync(HttpClient api, int employeeId, string date)
    {
        var response = await api.PostAsync($"/employees/{employeeId}/timecard/approve?date={date}", null, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<TimecardResponse>(body, TestJson.Options)!;
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
}
