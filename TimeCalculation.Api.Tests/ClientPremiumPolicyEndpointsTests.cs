using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Model.Premiums;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class ClientPremiumPolicyEndpointsTests(ApiFixture fixture)
{
    private static LocalDate Date(string iso) => LocalDate.FromDateOnly(DateOnly.Parse(iso));

    [Fact]
    public async Task CreatePolicy_UnregisteredPremiumCode_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "NOT_A_REAL_CODE",
            WaiverPolicy = WaiverPolicy.SupervisorOnly,
            EffectiveFrom = Date("2026-01-01"),
        };

        var response = await api.PostAsJsonAsync("/clientpremiumpolicies", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_AsEmployee_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.SupervisorOnly,
            EffectiveFrom = Date("2026-01-01"),
        };

        var response = await employeeApi.PostAsJsonAsync("/clientpremiumpolicies", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_EffectiveToBeforeEffectiveFrom_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.SupervisorOnly,
            EffectiveFrom = Date("2026-01-10"),
            EffectiveTo = Date("2026-01-01"),
        };

        var response = await api.PostAsJsonAsync("/clientpremiumpolicies", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetListPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.SupervisorOnly,
            EffectiveFrom = Date("2026-01-01"),
            Justification = "Ops director sign-off, see ticket #123.",
        };
        var createResponse = await api.PostAsJsonAsync("/clientpremiumpolicies", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ClientPremiumPolicyResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        // SetBy/SetAt are server-derived, never client-supplied (request contract has no such fields
        // at all — this just confirms the response actually carries a real value, not a default).
        Assert.False(string.IsNullOrEmpty(created.SetBy));
        Assert.True(created.SetAt > Instant.MinValue);

        var getResponse = await api.GetAsync($"/clientpremiumpolicies/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await api.GetAsync($"/clientpremiumpolicies?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<ClientPremiumPolicyResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, p => p.Id == created.Id);

        var updateRequest = new UpdateClientPremiumPolicyRequest
        {
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.BothRequired,
            EffectiveFrom = Date("2026-01-01"),
            Justification = "Tightened after audit.",
        };
        var putResponse = await api.PutAsJsonAsync($"/clientpremiumpolicies/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<ClientPremiumPolicyResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(WaiverPolicy.BothRequired, updated!.WaiverPolicy);

        var deleteResponse = await api.DeleteAsync($"/clientpremiumpolicies/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await api.GetAsync($"/clientpremiumpolicies/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_OverlapsExistingPolicyForSameCode_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var firstRequest = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.SupervisorOnly,
            EffectiveFrom = Date("2026-01-01"),
        };
        var firstResponse = await api.PostAsJsonAsync("/clientpremiumpolicies", firstRequest, TestJson.Options, TestContext.Current.CancellationToken);
        firstResponse.EnsureSuccessStatusCode();

        var overlappingRequest = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientId,
            PremiumCode = "CA_MEAL",
            WaiverPolicy = WaiverPolicy.BothRequired,
            EffectiveFrom = Date("2026-06-01"),   // still open-ended, so it overlaps the first row
        };
        var overlappingResponse = await api.PostAsJsonAsync("/clientpremiumpolicies", overlappingRequest, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, overlappingResponse.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_SamePremiumCodeDifferentClient_DoesNotConflict()
    {
        // The overlap check is scoped per client, not globally by premium code — two different
        // clients each get their own CA_MEAL policy on the same dates.
        var (clientAId, apiA) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientBId, apiB) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var requestA = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientAId, PremiumCode = "CA_MEAL", WaiverPolicy = WaiverPolicy.SupervisorOnly, EffectiveFrom = Date("2026-01-01"),
        };
        var requestB = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientBId, PremiumCode = "CA_MEAL", WaiverPolicy = WaiverPolicy.EmployeeOnly, EffectiveFrom = Date("2026-01-01"),
        };

        var responseA = await apiA.PostAsJsonAsync("/clientpremiumpolicies", requestA, TestJson.Options, TestContext.Current.CancellationToken);
        var responseB = await apiB.PostAsJsonAsync("/clientpremiumpolicies", requestB, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task ScopedClient_CannotSeeAnotherClientsPolicy()
    {
        var (clientAId, apiA) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (_, apiB) = await fixture.CreateClientAndScopedClientAsync($"CPP Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var createRequest = new CreateClientPremiumPolicyRequest
        {
            ClientId = clientAId, PremiumCode = "CA_MEAL", WaiverPolicy = WaiverPolicy.SupervisorOnly, EffectiveFrom = Date("2026-01-01"),
        };
        var createResponse = await apiA.PostAsJsonAsync("/clientpremiumpolicies", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var created = (await createResponse.Content.ReadFromJsonAsync<ClientPremiumPolicyResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var response = await apiB.GetAsync($"/clientpremiumpolicies/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
