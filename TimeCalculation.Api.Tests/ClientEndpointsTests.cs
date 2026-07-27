using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
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
    public async Task UpdateClient_AsEmployee_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Auth Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new UpdateClientRequest { Name = "Renamed by employee" };

        var response = await employeeApi.PutAsJsonAsync($"/clients/{clientId}", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task SystemAdmin_CanGetUpdateAndDeleteAnExistingClient()
    {
        // Regression test for a bug that reached the UI: the Client tenant filter is
        // `c.Id == _tenantClientId`, and a SystemAdmin carries no custom:client_id claim by design,
        // so every by-id read matched nothing and returned 404 — for the one role whose whole job is
        // managing clients. The existing coverage missed it entirely because the lifecycle test runs
        // as a ClientAdmin scoped to that exact client, and the only SystemAdmin case asserted 404
        // for a NONEXISTENT id, which passed no matter what. Asserting against a client that really
        // exists is the check that has teeth.
        var name = $"SysAdmin Visible Co {Guid.NewGuid()}";
        var created = await CreateAsync(name);

        var getResponse = await fixture.SystemAdminClient.GetAsync($"/clients/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(name, fetched!.Name);

        var putResponse = await fixture.SystemAdminClient.PutAsJsonAsync(
            $"/clients/{created.Id}", new UpdateClientRequest { Name = $"{name} Renamed" }, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var deleteResponse = await fixture.SystemAdminClient.DeleteAsync($"/clients/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Soft-deleted rows stay invisible even to SystemAdmin — IgnoreQueryFilters drops the
        // soft-delete filter along with the tenant one, so this proves !IsDeleted was re-applied.
        var afterDelete = await fixture.SystemAdminClient.GetAsync($"/clients/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task GetClient_AsClientAdminOfAnotherClient_Returns404()
    {
        // The other half: widening visibility for SystemAdmin must not widen it for anyone else.
        var target = await CreateAsync($"Isolation Target Co {Guid.NewGuid()}");
        var (_, otherApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Other Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await otherApi.GetAsync($"/clients/{target.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async Task<ClientResponse> CreateAsync(string name)
    {
        var response = await fixture.SystemAdminClient.PostAsJsonAsync(
            "/clients", new CreateClientRequest { Name = name }, TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
    }
}
