using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class HolidayCalendarEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateHolidayCalendar_MissingName_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Holiday Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new CreateHolidayCalendarRequest { ClientId = clientId, Name = "" };

        var response = await api.PostAsJsonAsync("/holidaycalendars", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateHolidayCalendar_AsEmployee_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Holiday Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new CreateHolidayCalendarRequest { ClientId = clientId, Name = "Federal" };

        var response = await employeeApi.PostAsJsonAsync("/holidaycalendars", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateGetPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Holiday Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createRequest = new CreateHolidayCalendarRequest
        {
            ClientId = clientId,
            Name = "Federal",
            Dates = [new LocalDate(2026, 1, 1), new LocalDate(2026, 12, 25)],
        };
        var createResponse = await api.PostAsJsonAsync("/holidaycalendars", createRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<HolidayCalendarResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(2, created.Dates.Count);

        var listResponse = await api.GetAsync($"/holidaycalendars?clientId={clientId}", TestContext.Current.CancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HolidayCalendarResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(page!.Items, h => h.Id == created.Id);

        var updateRequest = new UpdateHolidayCalendarRequest
        {
            Name = "Federal + State",
            Dates = [new LocalDate(2026, 1, 1), new LocalDate(2026, 12, 25), new LocalDate(2026, 7, 4)],
        };
        var putResponse = await api.PutAsJsonAsync($"/holidaycalendars/{created.Id}", updateRequest, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<HolidayCalendarResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("Federal + State", updated!.Name);
        Assert.Equal(3, updated.Dates.Count);

        var deleteResponse = await api.DeleteAsync($"/holidaycalendars/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await api.GetAsync($"/holidaycalendars/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteHolidayCalendar_ReferencedByPayRule_Returns409AndNamesTheRule()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Holiday Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var createResponse = await api.PostAsJsonAsync(
            "/holidaycalendars", new CreateHolidayCalendarRequest { ClientId = clientId, Name = "Federal" },
            TestJson.Options, TestContext.Current.CancellationToken);
        var created = (await createResponse.Content.ReadFromJsonAsync<HolidayCalendarResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var payRuleName = $"Rule {Guid.NewGuid()}";
        var payRuleResponse = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = payRuleName, HolidayCalendarId = created.Id },
            TestJson.Options, TestContext.Current.CancellationToken);
        payRuleResponse.EnsureSuccessStatusCode();

        var deleteResponse = await api.DeleteAsync($"/holidaycalendars/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        var body = await deleteResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(payRuleName, body);

        var getAfterFailedDelete = await api.GetAsync($"/holidaycalendars/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
    }

    [Fact]
    public async Task GetUsFederalHolidays_ReturnsElevenObservedDates()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Holiday Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.GetFromJsonAsync<List<LocalDate>>(
            "/metadata/us-federal-holidays?year=2026", TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(11, response!.Count);
        Assert.Contains(new LocalDate(2026, 12, 25), response);
    }
}
