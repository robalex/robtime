using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PositionEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreatePosition_NegativeBaseRate_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Position Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreatePositionRequest { ClientId = clientId, Code = "X", Name = "X", BaseRate = -1m };

        var response = await api.PostAsJsonAsync("/positions", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Position Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreatePositionRequest { ClientId = clientId, Code = "COOK", Name = "Cook", BaseRate = 18m };
        var createResponse = await api.PostAsJsonAsync("/positions", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PositionResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var listResponse = await api.GetAsync($"/positions?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<PositionResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, p => p.Id == created.Id);

        var updateRequest = new UpdatePositionRequest { Code = "COOK", Name = "Head Cook", BaseRate = 21m };
        var putResponse = await api.PutAsJsonAsync($"/positions/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var deleteResponse = await api.DeleteAsync($"/positions/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await api.GetAsync($"/positions/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task CreatePosition_AsEmployee_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Position Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new CreatePositionRequest { ClientId = clientId, Code = "X", Name = "X", BaseRate = 15m };

        var response = await employeeApi.PostAsJsonAsync("/positions", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
