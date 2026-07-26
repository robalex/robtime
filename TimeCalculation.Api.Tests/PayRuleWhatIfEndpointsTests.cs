using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayRuleWhatIfEndpointsTests(ApiFixture fixture)
{
    private static LocalDate Date(string iso) => LocalDate.FromDateOnly(DateOnly.Parse(iso));

    [Fact]
    public async Task DraftTurnsOnADifferential_ShowsAsAChangedShiftWithHigherGross()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId, minimumWage: 20m);

        var nightCode = await CreateDifferentialRuleAsync(api, clientId, "NIGHT", adjustmentValue: 2m);

        var baselineId = await CreatePayRuleAsync(api, clientId, "Baseline", activeDifferentialCodes: []);
        await AssignPayRuleAsync(api, employeeId, baselineId, Date("2026-01-01"));

        var proposedId = await CreatePayRuleAsync(api, clientId, "Proposed", activeDifferentialCodes: [nightCode]);

        // One 8-hr shift, Monday Jan 5 2026, 09:00-17:00 UTC.
        await CreatePunchAsync(api, employeeId, Instant.FromUtc(2026, 1, 5, 9, 0), PunchKind.In);
        await CreatePunchAsync(api, employeeId, Instant.FromUtc(2026, 1, 5, 17, 0), PunchKind.Out);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{proposedId}/what-if",
            new WhatIfRequest { EmployeeId = employeeId, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-06") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatIfResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(baselineId, body!.Current.PayRuleId);
        Assert.Equal(proposedId, body.Draft.PayRuleId);
        // straight 8x20=160 either way; the draft additionally earns the all-day NIGHT differential: 8x2=16.
        Assert.Equal(160m, body.Current.GrossPay);
        Assert.Equal(176m, body.Draft.GrossPay);

        var diff = Assert.Single(body.ShiftDiffs);
        Assert.Equal(WhatIfShiftDiffStatus.Changed, diff.Status);
        Assert.Equal(16m, diff.Delta);
        Assert.Contains(diff.DraftLineItems, l => l.Code == "NIGHT" && l.Type == PayLineType.Differential);
        Assert.DoesNotContain(diff.CurrentLineItems, l => l.Code == "NIGHT");
    }

    [Fact]
    public async Task NoDifferenceBetweenConfigs_ShiftIsUnchanged()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId, minimumWage: 15m);

        var ruleAId = await CreatePayRuleAsync(api, clientId, "A", activeDifferentialCodes: []);
        await AssignPayRuleAsync(api, employeeId, ruleAId, Date("2026-01-01"));
        var ruleBId = await CreatePayRuleAsync(api, clientId, "B", activeDifferentialCodes: []);

        await CreatePunchAsync(api, employeeId, Instant.FromUtc(2026, 1, 5, 9, 0), PunchKind.In);
        await CreatePunchAsync(api, employeeId, Instant.FromUtc(2026, 1, 5, 17, 0), PunchKind.Out);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{ruleBId}/what-if",
            new WhatIfRequest { EmployeeId = employeeId, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-06") },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<WhatIfResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        var diff = Assert.Single(body!.ShiftDiffs);
        Assert.Equal(WhatIfShiftDiffStatus.Unchanged, diff.Status);
        Assert.Equal(0m, diff.Delta);
    }

    [Fact]
    public async Task PeriodEndNotAfterStart_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId, minimumWage: 15m);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", activeDifferentialCodes: []);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{payRuleId}/what-if",
            new WhatIfRequest { EmployeeId = employeeId, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-05") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPayRule_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId, minimumWage: 15m);

        var response = await api.PostAsJsonAsync(
            "/payrules/999999999/what-if",
            new WhatIfRequest { EmployeeId = employeeId, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-06") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEmployee_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", activeDifferentialCodes: []);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{payRuleId}/what-if",
            new WhatIfRequest { EmployeeId = 999999999, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-06") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoPayRuleAssignmentAtAll_CurrentFallsBackToDraftIdentity_AndCalculatesZero()
    {
        // An employee with no PayRuleAssignment history has no "current" — the draft still runs
        // (its synthetic assignment always covers the requested period), but there are no punches
        // either, so both sides settle at zero rather than either side throwing.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"WhatIf Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId, minimumWage: 15m);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Proposed", activeDifferentialCodes: []);

        var response = await api.PostAsJsonAsync(
            $"/payrules/{payRuleId}/what-if",
            new WhatIfRequest { EmployeeId = employeeId, PeriodStart = Date("2026-01-05"), PeriodEnd = Date("2026-01-06") },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhatIfResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(payRuleId, body!.Current.PayRuleId);   // fell back to the draft's own identity
        Assert.Equal(0m, body.Current.GrossPay);
        Assert.Equal(0m, body.Draft.GrossPay);
        Assert.Empty(body.ShiftDiffs);
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId, decimal minimumWage)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest
            {
                ClientId = clientId, FirstName = "Test", LastName = "Employee",
                MinimumWage = minimumWage, HomeTimeZoneId = "UTC",
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    private static async Task<int> CreatePayRuleAsync(
        HttpClient api, int clientId, string name, HashSet<string> activeDifferentialCodes)
    {
        var response = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = name, ActiveDifferentialCodes = activeDifferentialCodes },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payRule = await response.Content.ReadFromJsonAsync<PayRuleResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        return payRule!.Id;
    }

    private static async Task AssignPayRuleAsync(HttpClient api, int employeeId, int payRuleId, LocalDate effectiveFrom)
    {
        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest { PayRuleId = payRuleId, EffectiveFrom = effectiveFrom },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> CreateDifferentialRuleAsync(
        HttpClient api, int clientId, string code, decimal adjustmentValue)
    {
        var response = await api.PostAsJsonAsync(
            "/differentialrules",
            new CreateDifferentialRuleRequest
            {
                ClientId = clientId,
                Code = code,
                DayScheduleMode = DayScheduleMode.EveryDay,
                AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
                AdjustmentValue = adjustmentValue,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return code;
    }

    private static async Task CreatePunchAsync(HttpClient api, int employeeId, Instant punchTime, PunchKind kind)
    {
        var response = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = punchTime, Kind = kind },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
