using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class EmployeeEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateEmployee_UnknownClient_Returns404()
    {
        var request = new CreateEmployeeRequest
        {
            ClientId = 999999999,
            FirstName = "Test",
            LastName = "Employee",
            MinimumWage = 15m,
        };

        var response = await fixture.Client.PostAsJsonAsync("/employees", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateEmployee_NegativeMinimumWage_Returns400()
    {
        var clientId = await CreateClientAsync();
        var request = new CreateEmployeeRequest
        {
            ClientId = clientId,
            FirstName = "Test",
            LastName = "Employee",
            MinimumWage = -1m,
        };

        var response = await fixture.Client.PostAsJsonAsync("/employees", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var clientId = await CreateClientAsync();
        var createRequest = new CreateEmployeeRequest
        {
            ClientId = clientId, FirstName = "Jane", LastName = "Doe", MinimumWage = 15m,
        };
        var createResponse = await fixture.Client.PostAsJsonAsync("/employees", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var updateRequest = new UpdateEmployeeRequest
        {
            FirstName = "Jane", LastName = "Smith", MinimumWage = 16m,
        };
        var putResponse = await fixture.Client.PutAsJsonAsync($"/employees/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("Smith", updated!.LastName);

        var deleteResponse = await fixture.Client.DeleteAsync($"/employees/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private async Task<int> CreateClientAsync()
    {
        var request = new CreateClientRequest { Name = $"Employee Test Co {Guid.NewGuid()}", CreatedBy = "test" };
        var response = await fixture.Client.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return body!.Id;
    }
}
