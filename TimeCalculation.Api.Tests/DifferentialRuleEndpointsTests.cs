using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class DifferentialRuleEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateDifferentialRule_MissingCode_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "",
            DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
        };

        var response = await api.PostAsJsonAsync("/differentialrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDifferentialRule_NegativeAdjustmentValue_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = -1m,
        };

        var response = await api.PostAsJsonAsync("/differentialrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDifferentialRule_ConsecutiveDayRangeWithSameStartAndEnd_Returns400()
    {
        // Mirrors PipelineContext's own rejection of a single-day "range" — should be a DaysOfWeek
        // selection instead.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Saturday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Saturday,
            AdjustmentType = DifferentialAdjustmentType.Multiplier,
            AdjustmentValue = 0.1m,
        };

        var response = await api.PostAsJsonAsync("/differentialrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDifferentialRule_DaysOfWeekModeWithNoDaysSelected_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "WEEKEND",
            DayScheduleMode = DayScheduleMode.DaysOfWeek,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 1.5m,
        };

        var response = await api.PostAsJsonAsync("/differentialrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.EveryDay,
            WindowStart = new LocalTime(18, 0),
            WindowEnd = new LocalTime(6, 0),
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2.50m,
        };
        var createResponse = await api.PostAsJsonAsync("/differentialrules", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<DifferentialRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.False(created.IsAllDay);

        var listResponse = await api.GetAsync($"/differentialrules?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<DifferentialRuleResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, d => d.Id == created.Id);

        var updateRequest = new UpdateDifferentialRuleRequest
        {
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 3.00m,
        };
        var putResponse = await api.PutAsJsonAsync($"/differentialrules/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<DifferentialRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(3.00m, updated!.AdjustmentValue);
        Assert.True(updated.IsAllDay);

        var deleteResponse = await api.DeleteAsync($"/differentialrules/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await api.GetAsync($"/differentialrules/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task CreateDifferentialRule_ConsecutiveDayRange_RoundTripsRangeAndMinHours()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "LONG_WEEKEND",
            DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Thursday,
            DayOfWeekRangeEnd = IsoDayOfWeek.Tuesday,
            MinHoursInRange = 20m,
            AdjustmentType = DifferentialAdjustmentType.Multiplier,
            AdjustmentValue = 0.15m,
        };

        var response = await api.PostAsJsonAsync("/differentialrules", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DifferentialRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(IsoDayOfWeek.Thursday, created!.DayOfWeekRangeStart);
        Assert.Equal(IsoDayOfWeek.Tuesday, created.DayOfWeekRangeEnd);
        Assert.Equal(20m, created.MinHoursInRange);
    }

    [Fact]
    public async Task DeleteDifferentialRule_ReferencedByPayRule_Returns409AndNamesTheRule()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Diff Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreateDifferentialRuleRequest
        {
            ClientId = clientId,
            Code = "NIGHT",
            DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
            AdjustmentValue = 2m,
        };
        var createResponse = await api.PostAsJsonAsync("/differentialrules", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var created = (await createResponse.Content.ReadFromJsonAsync<DifferentialRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var payRuleName = $"Rule {Guid.NewGuid()}";
        var payRuleResponse = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = payRuleName, ActiveDifferentialCodes = ["NIGHT"] },
            TestJson.Options, TestContext.Current.CancellationToken);
        payRuleResponse.EnsureSuccessStatusCode();

        var deleteResponse = await api.DeleteAsync($"/differentialrules/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        var body = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(payRuleName, body);

        var getAfterFailedDelete = await api.GetAsync($"/differentialrules/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
    }
}
