using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>The reference pattern every other entity's CRUD tests copy — full lifecycle plus the
/// failure modes (validation, not-found). List/Create are SystemAdmin-only (UI_PLAN.md §5's
/// SystemAdmin-scoping decision — creating/listing clients is the one genuinely cross-tenant
/// action); Get/Update/Delete run under a ClientAdmin scoped to the client under test, same as every
/// other entity's tests.</summary>
[Collection("Api")]
public class ClientEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateClient_Valid_Returns201WithLocation()
    {
        var request = new CreateClientRequest { Name = $"Test Co {Guid.NewGuid()}" };

        var response = await fixture.SystemAdminClient.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(request.Name, body.Name);
        Assert.True(body.Id > 0);
    }

    [Fact]
    public async Task CreateClient_Unauthenticated_Returns401()
    {
        var request = new CreateClientRequest { Name = $"Test Co {Guid.NewGuid()}" };

        var response = await fixture.Client.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_BlankName_Returns400ValidationProblem()
    {
        var request = new CreateClientRequest { Name = "" };

        var response = await fixture.SystemAdminClient.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FullLifecycle_CreateListGetPutDelete_BehavesCorrectly()
    {
        var name = $"Lifecycle Co {Guid.NewGuid()}";
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(name, TestContext.Current.CancellationToken);

        // List — SystemAdmin-only, filtered by our own unique name since the table is shared.
        var listResponse = await fixture.SystemAdminClient.GetAsync(
            $"/clients?search={Uri.EscapeDataString(name)}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<ClientResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(page);
        Assert.Single(page.Items, c => c.Id == clientId);

        // Get — as that client's own ClientAdmin.
        var getResponse = await api.GetAsync($"/clients/{clientId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Put
        var updateRequest = new UpdateClientRequest { Name = $"{name} Updated" };
        var putResponse = await api.PutAsJsonAsync($"/clients/{clientId}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal($"{name} Updated", updated!.Name);

        // Delete
        var deleteResponse = await api.DeleteAsync($"/clients/{clientId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Get after delete — soft-deleted rows are filtered out
        var getAfterDelete = await api.GetAsync($"/clients/{clientId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
        Assert.Equal("application/problem+json", getAfterDelete.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetClient_NonExistentId_Returns404Problem()
    {
        var response = await fixture.SystemAdminClient.GetAsync("/clients/999999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeleteClient_NonExistentId_Returns404()
    {
        var response = await fixture.SystemAdminClient.DeleteAsync("/clients/999999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
