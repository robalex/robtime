using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var clientId = await CreateClientAsync();
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = "" };

        var response = await fixture.Client.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePayRule_Valid_RuleFamilyIdEqualsOwnId()
    {
        // Gap F's versioning convention: a first-created version's RuleFamilyId equals its own Id,
        // set via a two-phase save in PayRuleService — this is the thing that could silently regress
        // to 0 (the unsaved default) if that second save were ever accidentally dropped.
        var clientId = await CreateClientAsync();
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = $"Rule {Guid.NewGuid()}" };

        var response = await fixture.Client.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);
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
        var clientId = await CreateClientAsync();
        var request = new CreatePayRuleRequest
        {
            ClientId = clientId,
            Name = $"Rule {Guid.NewGuid()}",
            RoundingStrategy = RoundingStrategy.IntervalWithGrace,
            RoundingIntervalMinutes = 10,
            RoundingGraceMinutes = 8,   // > half of 10 — invalid
        };

        var response = await fixture.Client.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePayRule_WhileDraft_Succeeds()
    {
        var clientId = await CreateClientAsync();
        var created = await CreatePayRuleAsync(clientId);

        var update = new UpdatePayRuleRequest { Name = "Updated Name" };
        var response = await fixture.Client.PutAsJsonAsync($"/payrules/{created.Id}", update, TestJson.Options, TestContext.Current.CancellationToken);

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
        var clientId = await CreateClientAsync();
        var created = await CreatePayRuleAsync(clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var update = new UpdatePayRuleRequest { Name = "Should Not Apply" };
        var response = await fixture.Client.PutAsJsonAsync($"/payrules/{created.Id}", update, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeletePayRule_WhileActive_Returns409Conflict()
    {
        var clientId = await CreateClientAsync();
        var created = await CreatePayRuleAsync(clientId);
        await SetStatusAsync(created.Id, PayRuleStatus.Active);

        var response = await fixture.Client.DeleteAsync($"/payrules/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeletePayRule_WhileDraft_Succeeds()
    {
        var clientId = await CreateClientAsync();
        var created = await CreatePayRuleAsync(clientId);

        var response = await fixture.Client.DeleteAsync($"/payrules/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task SetStatusAsync(int payRuleId, PayRuleStatus status)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var payRule = await db.PayRules.SingleAsync(r => r.Id == payRuleId);
        payRule.Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<PayRuleResponse> CreatePayRuleAsync(int clientId)
    {
        var request = new CreatePayRuleRequest { ClientId = clientId, Name = $"Rule {Guid.NewGuid()}" };
        var response = await fixture.Client.PostAsJsonAsync("/payrules", request, TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
    }

    private async Task<int> CreateClientAsync()
    {
        var request = new CreateClientRequest { Name = $"PayRule Test Co {Guid.NewGuid()}", CreatedBy = "test" };
        var response = await fixture.Client.PostAsJsonAsync("/clients", request, TestJson.Options, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return body!.Id;
    }
}
