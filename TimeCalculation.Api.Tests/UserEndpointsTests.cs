using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class UserEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateUser_AsOwnClientAdmin_Returns201()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"User Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateUserRequest
        {
            Email = $"user-{Guid.NewGuid()}@example.com", ClientId = clientId, DisplayName = "Test User", Role = AppRole.Supervisor,
        };

        var response = await api.PostAsJsonAsync("/users", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(clientId, body.ClientId);
        Assert.Equal(AppRole.Supervisor, body.Role);
        Assert.False(string.IsNullOrWhiteSpace(body.CognitoSub));
    }

    [Fact]
    public async Task CreateUser_ClientAdminTargetingAnotherClient_Returns403()
    {
        var (_, apiA) = await fixture.CreateClientAndScopedClientAsync($"User Test Co A {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientB, _) = await fixture.CreateClientAndScopedClientAsync($"User Test Co B {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateUserRequest
        {
            Email = $"user-{Guid.NewGuid()}@example.com", ClientId = clientB, DisplayName = "Test User", Role = AppRole.Supervisor,
        };

        var response = await apiA.PostAsJsonAsync("/users", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_MissingClientIdForNonSystemAdmin_Returns400()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"User Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateUserRequest
        {
            Email = $"user-{Guid.NewGuid()}@example.com", ClientId = null, DisplayName = "Test User", Role = AppRole.Supervisor,
        };

        var response = await api.PostAsJsonAsync("/users", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsSystemAdmin_CanTargetAnyClient_Returns201()
    {
        // Bootstrapping a brand-new client's first ClientAdmin — the cross-tenant exception
        // UI_PLAN.md §5 carves out for SystemAdmin, same as Client creation itself.
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"User Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateUserRequest
        {
            Email = $"user-{Guid.NewGuid()}@example.com", ClientId = clientId, DisplayName = "First Client Admin", Role = AppRole.ClientAdmin,
        };

        var response = await fixture.SystemAdminClient.PostAsJsonAsync("/users", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Unauthenticated_Returns401()
    {
        var request = new CreateUserRequest
        {
            Email = $"user-{Guid.NewGuid()}@example.com", ClientId = 1, DisplayName = "Test User", Role = AppRole.Supervisor,
        };

        var response = await fixture.Client.PostAsJsonAsync("/users", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
