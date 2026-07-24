using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PunchEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreatePunch_FixedDollarWithNoAmount_Returns400()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.FixedDollar,
        };

        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_UnknownEmployee_Returns404()
    {
        var (_, api, _) = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = 999999999,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
        };

        var response = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_DuplicateDeviceIdempotencyKey_Returns409OnSecondAttempt()
    {
        var (_, api, employeeId) = await CreateEmployeeAsync();
        var deviceId = $"device-{Guid.NewGuid()}";
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
            DeviceId = deviceId,
            DevicePunchId = "abc123",
        };

        var first = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await api.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    private async Task<(int ClientId, HttpClient Api, int EmployeeId)> CreateEmployeeAsync()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync($"Punch Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var employeeRequest = new CreateEmployeeRequest
        {
            ClientId = clientId,
            FirstName = "Test",
            LastName = "Employee",
            MinimumWage = 15m,
        };
        var employeeResponse = await api.PostAsJsonAsync("/employees", employeeRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return (clientId, api, employee!.Id);
    }
}
