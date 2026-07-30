using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class DifferentialSandboxEndpointTests(ApiFixture fixture)
{
    private static LocalDate Date(string iso) => LocalDate.FromDateOnly(DateOnly.Parse(iso));

    [Fact]
    public async Task EveryDayAllDayRule_ProjectsOneZonePerDayInWindow()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(api, clientId, "ALLDAY", DayScheduleMode.EveryDay);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(7, body!.Zones.Count);
        Assert.All(body.Zones, z => Assert.Equal("ALLDAY", z.Code));
        Assert.Contains(body.Zones, z => z.Start == Instant.FromUtc(2026, 1, 5, 0, 0) && z.End == Instant.FromUtc(2026, 1, 6, 0, 0));
    }

    [Fact]
    public async Task RuleNotInPayRulesActiveDifferentialCodes_ProjectsNoZones()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        await CreateDifferentialRuleAsync(api, clientId, "OFF", DayScheduleMode.EveryDay);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []); // does not enable OFF

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Empty(body!.Zones);
    }

    [Fact]
    public async Task HolidaysMode_UsesTheSelectedHolidayCalendar()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(api, clientId, "HOLIDAY", DayScheduleMode.Holidays);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);
        var holidayCalendarId = await CreateHolidayCalendarAsync(api, clientId, "Test Calendar", [Date("2026-01-07")]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId, HolidayCalendarId = holidayCalendarId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        var zone = Assert.Single(body!.Zones);
        Assert.Equal(Instant.FromUtc(2026, 1, 7, 0, 0), zone.Start);
        Assert.Equal(Instant.FromUtc(2026, 1, 8, 0, 0), zone.End);
    }

    [Fact]
    public async Task HolidaysMode_WithNoHolidayCalendarSelected_ProjectsNoZones()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(api, clientId, "HOLIDAY", DayScheduleMode.Holidays);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Empty(body!.Zones);
    }

    [Fact]
    public async Task ConsecutiveDayRange_WrappingOccurrence_ProjectsAcrossWindowEdges()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(
            api, clientId, "WEEKEND", DayScheduleMode.ConsecutiveDayRange,
            rangeStart: IsoDayOfWeek.Thursday, rangeEnd: IsoDayOfWeek.Tuesday);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7, // Mon Jan 5 - Sun Jan 11, 2026
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(2, body!.Zones.Count); // one occurrence entering the window, one starting inside it
    }

    [Fact]
    public async Task DayCountNotSevenOrFourteen_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest { EmployeeId = employeeId, PayRuleId = payRuleId, WindowStart = Date("2026-01-05"), DayCount = 10 },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPayRule_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest { EmployeeId = employeeId, PayRuleId = 999999999, WindowStart = Date("2026-01-05"), DayCount = 7 },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEmployee_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest { EmployeeId = 999999999, PayRuleId = payRuleId, WindowStart = Date("2026-01-05"), DayCount = 7 },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownHolidayCalendar_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId, HolidayCalendarId = 999999999,
                WindowStart = Date("2026-01-05"), DayCount = 7,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_CannotRunSandbox_Returns403()
    {
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var supervisorApi = fixture.CreateAuthenticatedClient(AppRole.Supervisor, clientId, sub: $"test-supervisor-{Guid.NewGuid()}");

        var response = await supervisorApi.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest { EmployeeId = 1, PayRuleId = 1, WindowStart = Date("2026-01-05"), DayCount = 7 },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SandboxRun_WritesNoPunchRows()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(api, clientId, "ALLDAY", DayScheduleMode.EveryDay);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest { EmployeeId = employeeId, PayRuleId = payRuleId, WindowStart = Date("2026-01-05"), DayCount = 7 },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        await using var db = CreateContext(clientId);
        Assert.Equal(0, await db.Punches.CountAsync(p => p.EmployeeId == employeeId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestPunches_ExclusivityConflict_ReportsWinnerAndLoser()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId); // HomeTimeZoneId defaults to UTC
        var lowCode = await CreateDifferentialRuleAsync(
            api, clientId, "LOW", DayScheduleMode.EveryDay, adjustmentValue: 1m, exclusivityGroup: "G");
        var highCode = await CreateDifferentialRuleAsync(
            api, clientId, "HIGH", DayScheduleMode.EveryDay, adjustmentValue: 5m, exclusivityGroup: "G");
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [lowCode, highCode]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
                TestPunches =
                [
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 9, 0), Kind = PunchKind.In },
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 17, 0), Kind = PunchKind.Out },
                ],
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        var shift = Assert.Single(body!.Shifts);
        var loserEval = shift.Evaluations.Single(e => e.Code == "LOW");
        var winnerEval = shift.Evaluations.Single(e => e.Code == "HIGH");

        Assert.Equal(DifferentialOutcome.SupersededByExclusivityGroup, loserEval.Outcome);
        Assert.Equal("HIGH", loserEval.SupersededByCode);
        Assert.Equal(8m, loserEval.QualifyingHours); // still reports what it would have earned
        Assert.Equal(DifferentialOutcome.Applied, winnerEval.Outcome);
        Assert.Equal(40m, winnerEval.Amount); // 8h x $5
    }

    [Fact]
    public async Task TestPunches_RuleNotEnabledByPayRule_ReportsCorrectOutcome()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        await CreateDifferentialRuleAsync(api, clientId, "OFF", DayScheduleMode.EveryDay);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []); // does not enable OFF

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
                TestPunches =
                [
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 9, 0), Kind = PunchKind.In },
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 17, 0), Kind = PunchKind.Out },
                ],
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        var eval = Assert.Single(Assert.Single(body!.Shifts).Evaluations);
        Assert.Equal(DifferentialOutcome.NotEnabledByPayRule, eval.Outcome);
    }

    [Fact]
    public async Task TestPunches_SpringForwardGap_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", []);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
                TestPunches =
                [
                    // The US spring-forward date — 02:00-03:00 never happens in America/New_York.
                    new SandboxTestPunch
                    {
                        PunchTime = new LocalDateTime(2026, 3, 8, 2, 30),
                        PunchTimeZoneId = "America/New_York",
                        Kind = PunchKind.In,
                    },
                ],
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("testPunches[0].PunchTime", body);
    }

    [Fact]
    public async Task TestPunches_NeverPersisted()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Sandbox Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var code = await CreateDifferentialRuleAsync(api, clientId, "ALLDAY", DayScheduleMode.EveryDay);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "A", [code]);

        var response = await api.PostAsJsonAsync(
            "/differentials/sandbox",
            new DifferentialSandboxRequest
            {
                EmployeeId = employeeId, PayRuleId = payRuleId,
                WindowStart = Date("2026-01-05"), DayCount = 7,
                TestPunches =
                [
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 9, 0), Kind = PunchKind.In },
                    new SandboxTestPunch { PunchTime = new LocalDateTime(2026, 1, 5, 17, 0), Kind = PunchKind.Out },
                ],
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DifferentialSandboxResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotEmpty(body!.Shifts); // the run did produce shift data...

        await using var db = CreateContext(clientId);
        Assert.Equal(0, await db.Punches.CountAsync(p => p.EmployeeId == employeeId, TestContext.Current.CancellationToken)); // ...but wrote nothing
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m, HomeTimeZoneId = "UTC" },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    private static async Task<int> CreatePayRuleAsync(HttpClient api, int clientId, string name, HashSet<string> activeDifferentialCodes)
    {
        var response = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = name, ActiveDifferentialCodes = activeDifferentialCodes },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payRule = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return payRule!.Id;
    }

    private static async Task<string> CreateDifferentialRuleAsync(
        HttpClient api, int clientId, string code, DayScheduleMode mode,
        IsoDayOfWeek rangeStart = default, IsoDayOfWeek rangeEnd = default,
        decimal adjustmentValue = 2m, string? exclusivityGroup = null)
    {
        var response = await api.PostAsJsonAsync(
            "/differentialrules",
            new CreateDifferentialRuleRequest
            {
                ClientId = clientId,
                Code = code,
                DayScheduleMode = mode,
                DayOfWeekRangeStart = rangeStart,
                DayOfWeekRangeEnd = rangeEnd,
                AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
                AdjustmentValue = adjustmentValue,
                ExclusivityGroup = exclusivityGroup,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return code;
    }

    private static async Task<int> CreateHolidayCalendarAsync(HttpClient api, int clientId, string name, HashSet<LocalDate> dates)
    {
        var response = await api.PostAsJsonAsync(
            "/holidaycalendars",
            new CreateHolidayCalendarRequest { ClientId = clientId, Name = name, Dates = dates },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var calendar = await response.Content.ReadFromJsonAsync<HolidayCalendarResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return calendar!.Id;
    }

    private PayrollDbContext CreateContext(int? tenantId)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        return new PayrollDbContext(options, new FixedTenantContextAccessor(tenantId));
    }
}
