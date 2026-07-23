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
        var employeeId = await CreateEmployeeAsync();
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.FixedDollar,
            CreatedBy = "test",
        };

        var response = await fixture.Client.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_UnknownEmployee_Returns404()
    {
        var request = new CreatePunchRequest
        {
            EmployeeId = 999999999,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
            CreatedBy = "test",
        };

        var response = await fixture.Client.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatePunch_DuplicateDeviceIdempotencyKey_Returns409OnSecondAttempt()
    {
        var employeeId = await CreateEmployeeAsync();
        var deviceId = $"device-{Guid.NewGuid()}";
        var request = new CreatePunchRequest
        {
            EmployeeId = employeeId,
            PunchTime = SystemClock.Instance.GetCurrentInstant(),
            Kind = PunchKind.In,
            CreatedBy = "test",
            DeviceId = deviceId,
            DevicePunchId = "abc123",
        };

        var first = await fixture.Client.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await fixture.Client.PostAsJsonAsync("/punches", request, TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    private async Task<int> CreateEmployeeAsync()
    {
        var clientRequest = new CreateClientRequest { Name = $"Punch Test Co {Guid.NewGuid()}", CreatedBy = "test" };
        var clientResponse = await fixture.Client.PostAsJsonAsync("/clients", clientRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var client = await clientResponse.Content.ReadFromJsonAsync<ClientResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        var employeeRequest = new CreateEmployeeRequest
        {
            ClientId = client!.Id,
            FirstName = "Test",
            LastName = "Employee",
            MinimumWage = 15m,
        };
        var employeeResponse = await fixture.Client.PostAsJsonAsync("/employees", employeeRequest, TestJson.Options, TestContext.Current.CancellationToken);
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }
}
