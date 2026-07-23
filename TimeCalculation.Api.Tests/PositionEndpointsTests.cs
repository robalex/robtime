using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PositionEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreatePosition_NegativeBaseRate_Returns400()
    {
        var clientId = await CreateClientAsync();
        var request = new CreatePositionRequest { ClientId = clientId, Code = "X", Name = "X", BaseRate = -1m };

        var response = await fixture.Client.PostAsJsonAsync("/positions", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var clientId = await CreateClientAsync();
        var createRequest = new CreatePositionRequest { ClientId = clientId, Code = "COOK", Name = "Cook", BaseRate = 18m };
        var createResponse = await fixture.Client.PostAsJsonAsync("/positions", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PositionResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var listResponse = await fixture.Client.GetAsync($"/positions?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<PositionResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, p => p.Id == created.Id);

        var updateRequest = new UpdatePositionRequest { Code = "COOK", Name = "Head Cook", BaseRate = 21m };
        var putResponse = await fixture.Client.PutAsJsonAsync($"/positions/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var deleteResponse = await fixture.Client.DeleteAsync($"/positions/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await fixture.Client.GetAsync($"/positions/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    private async Task<int> CreateClientAsync()
    {
        var request = new CreateClientRequest { Name = $"Position Test Co {Guid.NewGuid()}", CreatedBy = "test" };
        var response = await fixture.Client.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return body!.Id;
    }
}
