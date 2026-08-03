using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayrollExportProfileEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateProfile_AsEmployee_Returns403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var response = await employeeApi.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest { ClientId = clientId, Name = "ADP", Provider = PayrollProvider.Adp },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProfile_MissingName_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest { ClientId = clientId, Name = "", Provider = PayrollProvider.GenericCsv },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProfile_AdjustmentRowPolicyWithNoCode_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest
            {
                ClientId = clientId,
                Name = "ADP",
                Provider = PayrollProvider.Adp,
                RoundingPolicy = PayrollExportRoundingPolicy.AdjustmentRow,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AppliesDocumentedDefaults_WhenOptionalFieldsOmitted()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest { ClientId = clientId, Name = "ADP", Provider = PayrollProvider.Adp },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<PayrollExportProfileResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        Assert.Equal(PayrollExportGrouping.PayPeriod, created.Grouping);
        Assert.Equal(PayrollExportRoundingPolicy.DistributeRemainder, created.RoundingPolicy);
        Assert.Equal(2, created.AmountScale);
        Assert.Equal(2, created.HoursScale);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetListPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var createResponse = await api.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest { ClientId = clientId, Name = "ADP", Provider = PayrollProvider.Adp },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PayrollExportProfileResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        var getResponse = await api.GetAsync($"/payroll-export-profiles/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await api.GetAsync($"/payroll-export-profiles?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<PayrollExportProfileResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, p => p.Id == created.Id);

        var updateResponse = await api.PutAsJsonAsync(
            $"/payroll-export-profiles/{created.Id}",
            new UpdatePayrollExportProfileRequest { Name = "ADP WFN", Provider = PayrollProvider.Adp },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PayrollExportProfileResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("ADP WFN", updated!.Name);

        var deleteResponse = await api.DeleteAsync($"/payroll-export-profiles/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await api.GetAsync($"/payroll-export-profiles/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteProfile_WithMappingStillAttached_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await CreateProfileAsync(api, clientId);

        var mappingResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Regular, LineCode = "", EarningCode = "REG", ValueBasis = PayrollExportValueBasis.Hours,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        mappingResponse.EnsureSuccessStatusCode();

        var deleteResponse = await api.DeleteAsync($"/payroll-export-profiles/{profileId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ScopedClient_CannotSeeAnotherClientsProfile()
    {
        var (_, apiA) = await fixture.CreateClientAndScopedClientAsync($"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientBId, apiB) = await fixture.CreateClientAndScopedClientAsync($"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var profileBId = await CreateProfileAsync(apiB, clientBId);

        var response = await apiA.GetAsync($"/payroll-export-profiles/{profileBId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static async Task<int> CreateProfileAsync(HttpClient api, int clientId, string name = "ADP")
    {
        var response = await api.PostAsJsonAsync(
            "/payroll-export-profiles",
            new CreatePayrollExportProfileRequest { ClientId = clientId, Name = name, Provider = PayrollProvider.Adp },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<PayrollExportProfileResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return profile!.Id;
    }
}
