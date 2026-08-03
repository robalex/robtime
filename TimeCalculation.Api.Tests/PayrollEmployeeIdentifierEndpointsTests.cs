using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayrollEmployeeIdentifierEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateIdentifier_AsEmployee_Returns403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var response = await employeeApi.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = "ADP-001" },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateIdentifier_EmployeeFromAnotherClient_Returns404()
    {
        var (clientAId, apiA) = await fixture.CreateClientAndScopedClientAsync($"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientBId, apiB) = await fixture.CreateClientAndScopedClientAsync($"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileAId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(apiA, clientAId);
        var employeeBId = await CreateEmployeeAsync(apiB, clientBId);

        var response = await apiA.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileAId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeBId, ExternalEmployeeId = "ADP-001" },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has,a,comma")]
    public async Task CreateIdentifier_InvalidExternalId_Returns400(string externalId)
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = externalId },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateListPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var createResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = "  ADP-001  " },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PayrollEmployeeIdentifierResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal("ADP-001", created.ExternalEmployeeId);   // trimmed server-side

        var listResponse = await api.GetAsync($"/payroll-export-profiles/{profileId}/employee-identifiers", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<List<PayrollEmployeeIdentifierResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(list!, i => i.Id == created.Id);

        var updateResponse = await api.PutAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers/{created.Id}",
            new UpdatePayrollEmployeeIdentifierRequest { ExternalEmployeeId = "ADP-002" },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PayrollEmployeeIdentifierResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("ADP-002", updated!.ExternalEmployeeId);

        var deleteResponse = await api.DeleteAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateIdentifier_SameEmployeeTwiceOnOneProfile_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var first = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = "ADP-001" },
            TestJson.Options, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var second = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = employeeId, ExternalEmployeeId = "ADP-002" },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateIdentifier_SameExternalIdForTwoEmployees_Returns409()
    {
        // The constraint that actually protects a paycheck: two different RobTime employees must
        // never resolve to the same provider id, or their pay would silently merge into one payment.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var firstEmployeeId = await CreateEmployeeAsync(api, clientId);
        var secondEmployeeId = await CreateEmployeeAsync(api, clientId);

        var first = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = firstEmployeeId, ExternalEmployeeId = "ADP-SHARED" },
            TestJson.Options, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var second = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/employee-identifiers",
            new CreatePayrollEmployeeIdentifierRequest { EmployeeId = secondEmployeeId, ExternalEmployeeId = "ADP-SHARED" },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }
}
