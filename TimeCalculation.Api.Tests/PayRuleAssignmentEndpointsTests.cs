using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayRuleAssignmentEndpointsTests(ApiFixture fixture)
{
    private static LocalDate Date(string iso) => LocalDate.FromDateOnly(DateOnly.Parse(iso));

    [Fact]
    public async Task CreateAssignment_AsEmployee_Returns403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Assign Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var response = await employeeApi.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = 999999999, EffectiveFrom = Date("2026-01-01") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndList_ReturnsAssignmentWithPayRuleDetails()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Assign Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Federal Standard");

        var created = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = payRuleId, EffectiveFrom = Date("2026-01-01") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<PayRuleAssignmentResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        // Pay rule details are denormalised onto the response so a timeline can label rows without a
        // second request.
        Assert.Equal("Federal Standard", body!.PayRuleName);
        Assert.Equal(PayRuleStatus.Draft, body.PayRuleStatus);
        Assert.Null(body.EffectiveTo);

        var list = await api.GetFromJsonAsync<List<PayRuleAssignmentResponse>>(
            $"/employees/{employeeId}/payrules", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Single(list!);
    }

    [Fact]
    public async Task CreateOverlapping_Returns409()
    {
        // The core invariant: one pay rule at a time, same as position (decided 2026-07-25). Without
        // it, PipelineContext.GetRuleAt's first-match-wins resolution becomes order-dependent.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Overlap Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var federalId = await CreatePayRuleAsync(api, clientId, "Federal Standard");
        var californiaId = await CreatePayRuleAsync(api, clientId, "California");

        await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = federalId, EffectiveFrom = Date("2026-01-01"), EffectiveTo = Date("2026-06-30"),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var overlapping = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = californiaId, EffectiveFrom = Date("2026-06-30"), EffectiveTo = Date("2026-12-31"),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        // 2026-06-30 belongs to both — end dates are inclusive.
        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal("application/problem+json", overlapping.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateAdjacentWithNoSharedDay_Succeeds()
    {
        // The other side of the same boundary: succeeding one assignment with the next must work,
        // or the rule would make ordinary pay-rule changes impossible.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Adjacent Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var federalId = await CreatePayRuleAsync(api, clientId, "Federal Standard");
        var californiaId = await CreatePayRuleAsync(api, clientId, "California");

        await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = federalId, EffectiveFrom = Date("2026-01-01"), EffectiveTo = Date("2026-06-29"),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var next = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = californiaId, EffectiveFrom = Date("2026-06-30") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task UpdateAssignment_DoesNotConflictWithItself()
    {
        // Editing a row must exclude that row from the overlap check, or no assignment could ever be
        // edited — it always overlaps its own current dates.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"SelfEdit Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Federal Standard");

        var created = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = payRuleId, EffectiveFrom = Date("2026-01-01") },
            TestJson.Options, TestContext.Current.CancellationToken);
        var assignment = (await created.Content.ReadFromJsonAsync<PayRuleAssignmentResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        var updated = await api.PutAsJsonAsync(
            $"/employees/{employeeId}/payrules/{assignment.Id}",
            new UpdatePayRuleAssignmentRequest
            {
                PayRuleId = payRuleId, EffectiveFrom = Date("2026-01-01"), EffectiveTo = Date("2026-12-31"),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<PayRuleAssignmentResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(Date("2026-12-31"), body!.EffectiveTo);
    }

    [Fact]
    public async Task CreateWithEndBeforeStart_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"BadDates Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Federal Standard");

        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = payRuleId, EffectiveFrom = Date("2026-06-01"), EffectiveTo = Date("2026-01-01"),
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheAssignment()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"DeleteAssign Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Federal Standard");

        var created = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = payRuleId, EffectiveFrom = Date("2026-01-01") },
            TestJson.Options, TestContext.Current.CancellationToken);
        var assignment = (await created.Content.ReadFromJsonAsync<PayRuleAssignmentResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        var deleted = await api.DeleteAsync(
            $"/employees/{employeeId}/payrules/{assignment.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await api.GetFromJsonAsync<List<PayRuleAssignmentResponse>>(
            $"/employees/{employeeId}/payrules", TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task ListForAnotherClientsEmployee_Returns404()
    {
        // Tenant isolation: another client's employee must be invisible, not merely forbidden.
        var (clientA, apiA) = await fixture.CreateClientAndScopedClientAsync(
            $"Iso A {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var (clientB, apiB) = await fixture.CreateClientAndScopedClientAsync(
            $"Iso B {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeOfB = await CreateEmployeeAsync(apiB, clientB);

        var response = await apiA.GetAsync(
            $"/employees/{employeeOfB}/payrules", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    private static async Task<int> CreatePayRuleAsync(HttpClient api, int clientId, string name)
    {
        var response = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = name },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payRule = await response.Content.ReadFromJsonAsync<PayRuleResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return payRule!.Id;
    }
}
