using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class MeEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var response = await fixture.Client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_AuthenticatedWithNoAppUserRow_ReturnsClaimsAndNotProvisioned()
    {
        // The bootstrap case: a Cognito user created in the console rather than through POST /users.
        // /me must still answer — the frontend needs to distinguish "signed in but not set up" from
        // "not signed in", and a 404 here would make those indistinguishable.
        var sub = $"unprovisioned-{Guid.NewGuid()}";
        var api = fixture.CreateAuthenticatedClient(AppRole.SystemAdmin, clientId: null, sub: sub);

        var response = await api.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(me);
        Assert.Equal(sub, me.CognitoSub);
        Assert.Equal(AppRole.SystemAdmin, me.Role);
        Assert.Null(me.ClientId);
        Assert.False(me.IsProvisioned);
        Assert.Null(me.DisplayName);
    }

    [Fact]
    public async Task GetMe_AfterProvisioning_ReturnsProfileAndIsProvisioned()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Me Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreateUserRequest
        {
            Email = $"me-{Guid.NewGuid()}@example.com",
            ClientId = clientId,
            DisplayName = "Provisioned Person",
            Role = AppRole.Supervisor,
        };
        var created = await api.PostAsJsonAsync("/users", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        var createdUser = (await created.Content.ReadFromJsonAsync<UserResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        // Authenticate AS the newly provisioned user — same sub Cognito would put in their token.
        var theirApi = fixture.CreateAuthenticatedClient(AppRole.Supervisor, clientId, createdUser.CognitoSub);
        var response = await theirApi.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(me);
        Assert.True(me.IsProvisioned);
        Assert.Equal("Provisioned Person", me.DisplayName);
        Assert.Equal(clientId, me.ClientId);
        Assert.Equal(AppRole.Supervisor, me.Role);
    }
}
