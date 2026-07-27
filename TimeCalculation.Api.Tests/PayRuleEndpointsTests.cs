using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayRuleEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreatePayRule_BlankName_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = "" };

        var response = await api.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePayRule_AsEmployee_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = $"Rule {Guid.NewGuid()}" };

        var response = await employeeApi.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatePayRule_Valid_RuleFamilyIdEqualsOwnId()
    {
        // Gap F's versioning convention: a first-created version's RuleFamilyId equals its own Id,
        // set via a two-phase save in PayRuleService — this is the thing that could silently regress
        // to 0 (the unsaved default) if that second save were ever accidentally dropped.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = $"Rule {Guid.NewGuid()}" };

        var response = await api.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(body.Id, body.RuleFamilyId);
        Assert.Equal(PayRuleStatus.Draft, body.Status);
        Assert.Equal(1, body.Version);
    }

    [Fact]
    public async Task CreatePayRule_InvalidRoundingGraceInterval_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreatePayRuleRequest
        {
            ClientId = clientId,
            Name = $"Rule {Guid.NewGuid()}",
            RoundingStrategy = RoundingStrategy.IntervalWithGrace,
            RoundingIntervalMinutes = 10,
            RoundingGraceMinutes = 8,   // > half of 10 — invalid
        };

        var response = await api.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePayRule_WhileDraft_Succeeds()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);

        var update = new UpdatePayRuleRequest { Name = "Updated Name" };
        var response = await api.PutAsJsonAsync($"/payrules/{created.Id}", update, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("Updated Name", body!.Name);
    }

    [Fact]
    public async Task UpdatePayRule_WhileActive_Returns409Conflict()
    {
        // The entire point of Gap F's versioning design: an Active rule is never mutated in place.
        // No API path moves a rule to Active yet (that's Phase 4 UI work), so this flips it directly
        // via the DbContext — exactly the kind of thing this test project's real DB access is for.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var update = new UpdatePayRuleRequest { Name = "Should Not Apply" };
        var response = await api.PutAsJsonAsync($"/payrules/{created.Id}", update, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeletePayRule_WhileActive_Returns409Conflict()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var response = await api.DeleteAsync($"/payrules/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeletePayRule_WhileDraft_Succeeds()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"PayRule Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);

        var response = await api.DeleteAsync($"/payrules/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task SetStatusAsync(int payRuleId, PayRuleStatus status)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        // IgnoreQueryFilters: this scope has no HTTP request behind it, so ITenantContextAccessor
        // resolves no ClientId — exactly the "reach into the DB directly, bypass the API and its
        // tenant scoping" case the filter rework's class doc comment calls out as legitimate for
        // test/system code, not something to work around with a fake per-request principal.
        var payRule = await db.PayRules.IgnoreQueryFilters().SingleAsync(r => r.Id == payRuleId);
        payRule.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<PayRuleResponse> CreatePayRuleAsync(HttpClient api, int clientId)
    {
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = $"Rule {Guid.NewGuid()}" };
        var response = await api.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task ActivatePayRule_WhileDraft_BecomesActive()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Activate Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{created.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(PayRuleStatus.Active, body!.Status);
        Assert.Equal(LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")), body.EffectiveFrom);
        Assert.Null(body.EffectiveTo);
    }

    [Fact]
    public async Task ActivatePayRule_WhileAlreadyActive_Returns409Conflict()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Activate Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{created.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ActivatePayRule_SupersedesThePreviousActiveVersionInTheFamily()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Activate Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var v1 = await CreatePayRuleAsync(api, clientId);
        await api.PostAsJsonAsync(
            $"/payrules/{v1.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);

        var forked = await api.PostAsync($"/payrules/{v1.Id}/versions", null, TestContext.Current.CancellationToken);
        var v2 = (await forked.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var activateV2 = await api.PostAsJsonAsync(
            $"/payrules/{v2.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, activateV2.StatusCode);

        var v1AfterSupersede = await api.GetFromJsonAsync<PayRuleResponse>(
            $"/payrules/{v1.Id}", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(PayRuleStatus.Superseded, v1AfterSupersede!.Status);
        // Adjacent, non-overlapping windows — v1 ends the day before v2 begins.
        Assert.Equal(LocalDate.FromDateOnly(DateOnly.Parse("2026-05-31")), v1AfterSupersede.EffectiveTo);
    }

    [Fact]
    public async Task ActivatePayRule_BeforeCurrentActivesEffectiveDate_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Activate Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var v1 = await CreatePayRuleAsync(api, clientId);
        await api.PostAsJsonAsync(
            $"/payrules/{v1.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);

        var forked = await api.PostAsync($"/payrules/{v1.Id}/versions", null, TestContext.Current.CancellationToken);
        var v2 = (await forked.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var activateV2 = await api.PostAsJsonAsync(
            $"/payrules/{v2.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, activateV2.StatusCode);
    }

    [Fact]
    public async Task CreateNewVersion_FromActive_ProducesADraftWithIncrementedVersion()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Fork Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var response = await api.PostAsync($"/payrules/{created.Id}/versions", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(PayRuleStatus.Draft, body!.Status);
        Assert.Equal(created.RuleFamilyId, body.RuleFamilyId);
        Assert.Equal(created.Version + 1, body.Version);
        Assert.NotEqual(created.Id, body.Id);
    }

    [Fact]
    public async Task CreateNewVersion_FromDraft_Returns409Conflict()
    {
        // Editing a Draft directly is the answer here, not forking another Draft from it.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Fork Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var created = await CreatePayRuleAsync(api, clientId);

        var response = await api.PostAsync($"/payrules/{created.Id}/versions", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateNewVersion_ForkedFromAnOldSupersededRow_StillGetsTheFamilysNextVersion()
    {
        // Forking from an out-of-date row (not the family's current max) must not produce a version
        // number collision with a newer version that already exists.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Fork Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var v1 = await CreatePayRuleAsync(api, clientId);
        await api.PostAsJsonAsync(
            $"/payrules/{v1.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-01-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);
        var v2Response = await api.PostAsync($"/payrules/{v1.Id}/versions", null, TestContext.Current.CancellationToken);
        var v2 = (await v2Response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        await api.PostAsJsonAsync(
            $"/payrules/{v2.Id}/activate",
            new ActivatePayRuleRequest { EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")) },
            TestJson.Options, TestContext.Current.CancellationToken);
        // v1 is now Superseded, v2 is Active.

        var v3Response = await api.PostAsync($"/payrules/{v1.Id}/versions", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, v3Response.StatusCode);
        var v3 = await v3Response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(3, v3!.Version);
    }
}
