using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class StateMinimumWageEndpointsTests(ApiFixture fixture)
{
    private static LocalDate Date(string iso) => LocalDate.FromDateOnly(DateOnly.Parse(iso));

    [Fact]
    public async Task ClientAdmin_CannotAccess_Returns403()
    {
        var (_, clientAdminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Wage Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await clientAdminApi.GetAsync("/state-minimum-wages", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateStateMinimumWage_MissingState_Returns400()
    {
        var request = new CreateStateMinimumWageRequest { State = "", EffectiveFrom = Date("2026-01-01"), Amount = 16m };

        var response = await fixture.SystemAdminClient.PostAsJsonAsync(
            "/state-minimum-wages", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateStateMinimumWage_OverlappingSameState_Returns409()
    {
        var state = $"Z{Guid.NewGuid():N}"[..8];
        var first = new CreateStateMinimumWageRequest
        {
            State = state, EffectiveFrom = Date("2026-01-01"), EffectiveTo = Date("2026-06-30"), Amount = 16m,
        };
        await fixture.SystemAdminClient.PostAsJsonAsync("/state-minimum-wages", first, TestJson.Options, TestContext.Current.CancellationToken);

        var overlapping = new CreateStateMinimumWageRequest
        {
            State = state, EffectiveFrom = Date("2026-06-30"), EffectiveTo = Date("2026-12-31"), Amount = 17m,
        };
        var response = await fixture.SystemAdminClient.PostAsJsonAsync(
            "/state-minimum-wages", overlapping, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var state = $"Z{Guid.NewGuid():N}"[..8];
        var createRequest = new CreateStateMinimumWageRequest
        {
            State = state, EffectiveFrom = Date("2026-01-01"), Amount = 16m,
        };
        var createResponse = await fixture.SystemAdminClient.PostAsJsonAsync(
            "/state-minimum-wages", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<StateMinimumWageResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Null(created.EffectiveTo);

        var listResponse = await fixture.SystemAdminClient.GetAsync(
            $"/state-minimum-wages?state={state}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<StateMinimumWageResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, w => w.Id == created.Id);

        var updateRequest = new UpdateStateMinimumWageRequest
        {
            State = state, EffectiveFrom = Date("2026-01-01"), Amount = 16.50m,
        };
        var putResponse = await fixture.SystemAdminClient.PutAsJsonAsync(
            $"/state-minimum-wages/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<StateMinimumWageResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(16.50m, updated!.Amount);

        var deleteResponse = await fixture.SystemAdminClient.DeleteAsync(
            $"/state-minimum-wages/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await fixture.SystemAdminClient.GetAsync(
            $"/state-minimum-wages/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }
}
