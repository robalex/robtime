using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class TimecardEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetTimecard_Succeeds_ReturnsCalculatedResult_WithFullItemizationForSupervisor()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        // Both punches land on the same local date as the ?date= query param below — guarantees
        // they fall inside whatever period PayPeriodCalculator resolves, regardless of the PayRule's
        // (default) frequency/anchor, without needing to replicate that period-boundary math here.
        // Employee's HomeTimeZoneId defaults to America/New_York (EmployeeService's own default),
        // and both instants below are safely mid-day there — no midnight-boundary risk.
        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timecard = await response.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(employeeId, timecard!.EmployeeId);
        Assert.Equal(payRuleId, timecard.PayRuleId);
        Assert.True(timecard.PeriodStart <= LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")));
        Assert.True(timecard.PeriodEnd >= LocalDate.FromDateOnly(DateOnly.Parse("2026-06-01")));
        Assert.True(timecard.GrossPay > 0);
        Assert.NotEmpty(timecard.Workweeks);

        // Supervisor+ (ClientAdmin, per CreateClientAndScopedClientAsync) always gets full
        // itemization regardless of ClientSettings — decision 19's own rule.
        Assert.Contains(timecard.Workweeks, w => w.RegularRate is not null);
    }

    [Fact]
    public async Task GetTimecard_NoAssignmentCoversDate_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetTimecard_UnknownEmployee_Returns404()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.GetAsync("/employees/999999999/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTimecard_InvalidDateParameter_Returns400()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=not-a-date", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTimecard_DateOmitted_DefaultsToTodayInEmployeeTimeZone()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        // Open-ended from far in the past — covers "today" whenever this test actually runs.
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        var response = await api.GetAsync($"/employees/{employeeId}/timecard", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CanViewOwnTimecard()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var payRuleId = await CreatePayRuleAsync(adminApi, clientId, "Standard");
        await AssignPayRuleAsync(adminApi, employeeId, payRuleId, "2020-01-01");
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, employeeId);

        var response = await employeeApi.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Employee_CannotViewSomeoneElsesTimecard_Returns403()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var myEmployeeId = await CreateEmployeeAsync(adminApi, clientId);
        var colleagueEmployeeId = await CreateEmployeeAsync(adminApi, clientId);
        var employeeApi = await CreateLinkedEmployeeClientAsync(clientId, myEmployeeId);

        var response = await employeeApi.GetAsync(
            $"/employees/{colleagueEmployeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EmployeeWithNoLinkedRecord_CannotViewAnyTimecard_Returns403()
    {
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var unlinkedApi = fixture.CreateAuthenticatedClient(
            AppRole.Employee, clientId, sub: $"test-unlinked-{Guid.NewGuid()}");

        var response = await unlinkedApi.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_CanStillViewAnyEmployeesTimecard()
    {
        // Regression guard: opening this route to the Employee policy must not narrow what a
        // Supervisor could already do.
        var (clientId, adminApi) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(adminApi, clientId);
        var payRuleId = await CreatePayRuleAsync(adminApi, clientId, "Standard");
        await AssignPayRuleAsync(adminApi, employeeId, payRuleId, "2020-01-01");

        var response = await adminApi.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>An Employee-role client whose sub has a real AppUser row linking it to
    /// <paramref name="employeeId"/> — same pattern as SelfServiceClockTests, duplicated here
    /// because these are the scoping rules for a different route, not the same test suite.</summary>
    private async Task<HttpClient> CreateLinkedEmployeeClientAsync(int clientId, int employeeId)
    {
        var sub = $"test-linked-employee-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNodaTime())
            .Options;
        await using var db = new PayrollDbContext(options, new FixedTenantContextAccessor(clientId));
        db.AppUsers.Add(new AppUser
        {
            CognitoSub = sub,
            ClientId = clientId,
            EmployeeId = employeeId,
            DisplayName = "Test Employee",
            Role = AppRole.Employee,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub);
    }

    [Fact]
    public async Task GetTimecard_ReturnsPunchPairDetail_NotJustPayItemization()
    {
        // Phase 6.5 / decision 25: the response has to carry in/out times, hours and position down at
        // the pair level, because after Phase 6.7 freezes a timecard this shape is all the screen gets
        // — a PayResult's AnchorPunchId alone can't be re-joined back to punches (PayCalculationDetail).
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        var timecard = await response.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        var workedDay = Assert.Single(timecard!.Workweeks.SelectMany(w => w.Days), d => d.Shifts.Count > 0);
        var shift = Assert.Single(workedDay.Shifts);
        var pair = Assert.Single(shift.Pairs);

        Assert.NotNull(pair.In);
        Assert.NotNull(pair.Out);
        Assert.Equal(InstantPattern.ExtendedIso.Parse("2026-06-01T13:00:00Z").Value, pair.In!.Time);
        Assert.Equal(InstantPattern.ExtendedIso.Parse("2026-06-01T21:00:00Z").Value, pair.Out!.Time);
        Assert.Equal(8m, pair.Hours);
        Assert.False(pair.IsIncomplete);
        Assert.Equal(8m, shift.TotalHours);
    }

    [Fact]
    public async Task GetTimecard_EmitsEmptyDays_AndSurfacesIncompletePair()
    {
        // An In with no Out: the pipeline keeps it as an orphan pair rather than discarding it, and
        // the timecard is where someone actually has to see it. The empty-day assertion covers the
        // other half — days with no punches are still rendered so the workweek boundaries the pay
        // breakdown groups by stay legible.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timecard = await response.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        var days = timecard!.Workweeks.SelectMany(w => w.Days).ToList();

        var incomplete = Assert.Single(days.SelectMany(d => d.Shifts).SelectMany(s => s.Pairs), p => p.IsIncomplete);
        Assert.NotNull(incomplete.In);
        Assert.Null(incomplete.Out);

        Assert.Contains(days, d => d.Shifts.Count == 0);
        Assert.All(days, d => Assert.InRange(d.Date, timecard.PeriodStart, timecard.PeriodEnd));
    }

    [Fact]
    public async Task GetTimecard_SpansMultipleWeeks_EmitsWeekWithNoShiftsAtAll()
    {
        // The default PayRule is BiWeekly (14 days), which always crosses at least one FLSA week
        // boundary regardless of anchor alignment — a single punch on one day can never cover every
        // week the period touches. Before the fix, WorkDayGrouper/WorkweekGrouper's "no shifts, no
        // week" behavior meant a week like that vanished from the response entirely rather than
        // rendering as empty days, which read as though the pay period were shorter than it is.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);
        var payRuleId = await CreatePayRuleAsync(api, clientId, "Standard");
        await AssignPayRuleAsync(api, employeeId, payRuleId, "2020-01-01");

        await CreatePunchAsync(api, employeeId, "2026-06-01T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-01T21:00:00Z", PunchKind.Out);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-01", TestContext.Current.CancellationToken);
        var timecard = await response.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        Assert.True(timecard!.Workweeks.Count >= 2, "a 14-day period should touch more than one FLSA week");
        Assert.Contains(timecard.Workweeks, w => w.Days.All(d => d.Shifts.Count == 0));

        // Every day in the period is represented exactly once across the returned weeks — guards the
        // per-week clipping in TimecardWorkweekResponse against overlapping or skipping days at a
        // week/period boundary.
        var allDates = timecard.Workweeks.SelectMany(w => w.Days).Select(d => d.Date).ToList();
        Assert.Equal(allDates.Distinct().Count(), allDates.Count);
        Assert.All(allDates, d => Assert.InRange(d, timecard.PeriodStart, timecard.PeriodEnd));
        Assert.Equal(Period.DaysBetween(timecard.PeriodStart, timecard.PeriodEnd) + 1, allDates.Count);
    }

    [Fact]
    public async Task GetTimecard_HolidaysModeDifferential_AppliesWhenPayRuleHasHolidayCalendar()
    {
        // The regression test for the holiday-differential gap: TimecardService.ResolvePeriodAsync
        // must actually attach the effective PayRule's HolidayCalendar to the PipelineContext it runs
        // — before this fix, DayScheduleMode.Holidays differentials silently never applied here.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Timecard Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeId = await CreateEmployeeAsync(api, clientId);

        var calendarResponse = await api.PostAsJsonAsync(
            "/holidaycalendars",
            new CreateHolidayCalendarRequest { ClientId = clientId, Name = "Federal", Dates = [new LocalDate(2026, 6, 19)] },
            TestJson.Options, TestContext.Current.CancellationToken);
        calendarResponse.EnsureSuccessStatusCode();
        var calendar = (await calendarResponse.Content.ReadFromJsonAsync<HolidayCalendarResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;

        var diffResponse = await api.PostAsJsonAsync(
            "/differentialrules",
            new CreateDifferentialRuleRequest
            {
                ClientId = clientId,
                Code = "HOLIDAY",
                DayScheduleMode = DayScheduleMode.Holidays,
                AdjustmentType = DifferentialAdjustmentType.FlatPerHour,
                AdjustmentValue = 5m,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        diffResponse.EnsureSuccessStatusCode();

        var payRuleResponse = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest
            {
                ClientId = clientId,
                Name = "Standard",
                ActiveDifferentialCodes = ["HOLIDAY"],
                HolidayCalendarId = calendar.Id,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        payRuleResponse.EnsureSuccessStatusCode();
        var payRule = (await payRuleResponse.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        await AssignPayRuleAsync(api, employeeId, payRule.Id, "2020-01-01");

        // Employee's HomeTimeZoneId defaults to America/New_York — 13:00Z/21:00Z stays inside
        // 2026-06-19 local, same as this file's other tests, so it lands squarely on the holiday.
        await CreatePunchAsync(api, employeeId, "2026-06-19T13:00:00Z", PunchKind.In);
        await CreatePunchAsync(api, employeeId, "2026-06-19T21:00:00Z", PunchKind.Out);

        var response = await api.GetAsync($"/employees/{employeeId}/timecard?date=2026-06-19", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var timecard = await response.Content.ReadFromJsonAsync<TimecardResponse>(TestJson.Options, TestContext.Current.CancellationToken);

        var lineItems = timecard!.Workweeks.SelectMany(w => w.Days).SelectMany(d => d.Shifts).SelectMany(s => s.LineItems).ToList();
        Assert.Contains(lineItems, li => li.Type == PayLineType.Differential && li.Code == "HOLIDAY");
    }

    private static async Task<int> CreateEmployeeAsync(HttpClient api, int clientId)
    {
        var response = await api.PostAsJsonAsync(
            "/employees",
            new CreateEmployeeRequest { ClientId = clientId, FirstName = "Test", LastName = "Employee", MinimumWage = 15m },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var employee = await response.Content.ReadFromJsonAsync<EmployeeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return employee!.Id;
    }

    private static async Task<int> CreatePayRuleAsync(HttpClient api, int clientId, string name)
    {
        var response = await api.PostAsJsonAsync(
            "/payrules",
            new CreatePayRuleRequest { ClientId = clientId, Name = name },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payRule = await response.Content.ReadFromJsonAsync<PayRuleResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return payRule!.Id;
    }

    private static async Task AssignPayRuleAsync(HttpClient api, int employeeId, int payRuleId, string effectiveFromIso)
    {
        var response = await api.PostAsJsonAsync(
            $"/employees/{employeeId}/payrules",
            new CreatePayRuleAssignmentRequest
            {
                PayRuleId = payRuleId,
                EffectiveFrom = LocalDate.FromDateOnly(DateOnly.Parse(effectiveFromIso)),
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task CreatePunchAsync(HttpClient api, int employeeId, string punchTimeIso, PunchKind kind)
    {
        var response = await api.PostAsJsonAsync(
            "/punches",
            new CreatePunchRequest { EmployeeId = employeeId, PunchTime = InstantPattern.ExtendedIso.Parse(punchTimeIso).Value, Kind = kind },
            TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
