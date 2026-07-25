using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// The SystemAdmin client selector (UI_PLAN.md §5): a SystemAdmin has no <c>custom:client_id</c>
/// claim, so its tenant comes from the <c>X-RobTime-Client-Id</c> request header instead.
///
/// The header being request-controlled is exactly why these tests exist. Honouring it for the wrong
/// role would turn one header into a cross-tenant read for any authenticated user, so "ignored for
/// everyone but SystemAdmin" is asserted directly rather than assumed from the implementation.
/// </summary>
[Collection("Api")]
public class ClientSelectionTests(ApiFixture fixture)
{
    [Fact]
    public async Task SystemAdminWithSelection_SeesThatClientsEmployees()
    {
        var (clientId, clientAdmin) = await fixture.CreateClientAndScopedClientAsync(
            $"Selection Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        await CreateEmployeeAsync(clientAdmin, clientId, "Selected");

        var systemAdmin = fixture.CreateAuthenticatedClient(
            AppRole.SystemAdmin, clientId: null, sub: $"sysadmin-{Guid.NewGuid()}", selectedClientId: clientId);

        var response = await systemAdmin.GetAsync($"/employees?clientId={clientId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<EmployeeResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, e => e.LastName == "Selected");
    }

    [Fact]
    public async Task SystemAdminWithoutSelection_SeesNothing()
    {
        // Fail closed: no selection resolves to no tenant, and the query filters turn that into zero
        // rows rather than every row.
        var (clientId, clientAdmin) = await fixture.CreateClientAndScopedClientAsync(
            $"Unselected Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        await CreateEmployeeAsync(clientAdmin, clientId, "Invisible");

        var systemAdmin = fixture.CreateAuthenticatedClient(
            AppRole.SystemAdmin, clientId: null, sub: $"sysadmin-{Guid.NewGuid()}");

        var response = await systemAdmin.GetAsync($"/employees?clientId={clientId}", TestContext.Current.CancellationToken);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<EmployeeResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task ClientAdminSpoofingTheSelectionHeader_StillSeesOnlyItsOwnClient()
    {
        // THE test for this feature. A ClientAdmin for A asks for B's employees and sets the
        // selection header to B. The header must be inert for their role — if this ever regresses,
        // any authenticated user can read every tenant by setting one header.
        var (clientA, adminA) = await fixture.CreateClientAndScopedClientAsync(
            $"Spoof A {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientB, adminB) = await fixture.CreateClientAndScopedClientAsync(
            $"Spoof B {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        await CreateEmployeeAsync(adminB, clientB, "SecretOfB");

        var spoofer = fixture.CreateAuthenticatedClient(
            AppRole.ClientAdmin, clientId: clientA, sub: $"spoofer-{Guid.NewGuid()}", selectedClientId: clientB);

        var response = await spoofer.GetAsync($"/employees?clientId={clientB}", TestContext.Current.CancellationToken);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<EmployeeResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(page!.Items, e => e.LastName == "SecretOfB");
        Assert.Empty(page.Items);

        // And their own client is unaffected — the header didn't override the claim in either
        // direction, it was simply ignored.
        await CreateEmployeeAsync(adminA, clientA, "BelongsToA");
        var ownResponse = await spoofer.GetAsync($"/employees?clientId={clientA}", TestContext.Current.CancellationToken);
        var ownPage = await ownResponse.Content.ReadFromJsonAsync<PagedResult<EmployeeResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(ownPage!.Items, e => e.LastName == "BelongsToA");
    }

    [Fact]
    public async Task Me_ReportsTheEffectiveClientAndItsName()
    {
        var name = $"Effective Co {Guid.NewGuid()}";
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync(name, TestContext.Current.CancellationToken);

        var systemAdmin = fixture.CreateAuthenticatedClient(
            AppRole.SystemAdmin, clientId: null, sub: $"sysadmin-{Guid.NewGuid()}", selectedClientId: clientId);

        var me = await (await systemAdmin.GetAsync("/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(clientId, me!.ClientId);
        Assert.Equal(name, me.ClientName);
    }

    [Fact]
    public async Task Me_WithSelectionPointingAtAMissingClient_ReportsIdButNoName()
    {
        // How the UI tells "stale selection" apart from "no data yet" — without this the app would
        // render empty screens everywhere with no way to explain why.
        var systemAdmin = fixture.CreateAuthenticatedClient(
            AppRole.SystemAdmin, clientId: null, sub: $"sysadmin-{Guid.NewGuid()}", selectedClientId: 999999999);

        var me = await (await systemAdmin.GetAsync("/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(999999999, me!.ClientId);
        Assert.Null(me.ClientName);
    }

    private static async Task CreateEmployeeAsync(HttpClient api, int clientId, string lastName)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = lastName, MinimumWage = 15m },
            TestJson.Options,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
