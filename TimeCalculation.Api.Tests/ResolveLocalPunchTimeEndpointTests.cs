using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// POST /punches/resolve-local-time — the endpoint the manual/bulk-entry UI calls to turn a local
/// date/time + zone picked in a &lt;input type="datetime-local"&gt; into the same DST-aware Instant
/// punch import already produces for CSV rows, via the shared LocalTimeResolver. Doesn't touch any
/// tenant data, so these tests only need an authenticated client, not an employee/punch fixture.
/// </summary>
[Collection("Api")]
public class ResolveLocalPunchTimeEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Resolve_UnambiguousLocalTime_ReturnsInstant()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new ResolveLocalPunchTimeRequest
        {
            PunchTime = LocalDateTime(2026, 6, 1, 13, 0),
            PunchTimeZoneId = "America/New_York",
        };

        var response = await api.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.Content.ReadFromJsonAsync<ResolveLocalPunchTimeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(Instant.FromUtc(2026, 6, 1, 17, 0, 0), resolved!.PunchTime); // EDT, UTC-4
    }

    [Fact]
    public async Task Resolve_SpringForwardGap_Returns400()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new ResolveLocalPunchTimeRequest
        {
            PunchTime = LocalDateTime(2026, 3, 8, 2, 30), // the US spring-forward date
            PunchTimeZoneId = "America/New_York",
        };

        var response = await api.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"PunchTime\"", body);
        Assert.Contains("does not exist", body);
    }

    [Fact]
    public async Task Resolve_FallBackAmbiguous_NoFlagGiven_Returns400()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new ResolveLocalPunchTimeRequest
        {
            PunchTime = LocalDateTime(2026, 11, 1, 1, 30), // the US fall-back date
            PunchTimeZoneId = "America/New_York",
        };

        var response = await api.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"DaylightSaving\"", body);
    }

    [Fact]
    public async Task Resolve_FallBackAmbiguous_TrueVsFalse_ResolveOneHourApart()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var local = LocalDateTime(2026, 11, 1, 1, 30);

        var daylight = await ResolveAsync(api, local, "America/New_York", daylightSaving: true);
        var standard = await ResolveAsync(api, local, "America/New_York", daylightSaving: false);

        Assert.Equal(Duration.FromHours(1), standard - daylight);
        Assert.True(daylight < standard);
    }

    [Fact]
    public async Task Resolve_NonDstZone_ResolvesWithoutDaylightSavingFlag()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        // Ambiguous in America/New_York, but UTC has no DST at all, so it resolves cleanly.
        var resolved = await ResolveAsync(api, LocalDateTime(2026, 11, 1, 1, 30), "UTC", daylightSaving: null);

        Assert.Equal(Instant.FromUtc(2026, 11, 1, 1, 30, 0), resolved);
    }

    [Fact]
    public async Task Resolve_SameLocalTime_ResolvesDifferentInstants_AcrossDstSeasons()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var summer = await ResolveAsync(api, LocalDateTime(2026, 6, 1, 13, 0), "America/New_York", daylightSaving: null);
        var winter = await ResolveAsync(api, LocalDateTime(2026, 1, 15, 13, 0), "America/New_York", daylightSaving: null);

        Assert.Equal(Instant.FromUtc(2026, 6, 1, 17, 0, 0), summer);
        Assert.Equal(Instant.FromUtc(2026, 1, 15, 18, 0, 0), winter);
    }

    [Fact]
    public async Task Resolve_InvalidTimeZoneId_Returns400()
    {
        var (_, api) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var request = new ResolveLocalPunchTimeRequest
        {
            PunchTime = LocalDateTime(2026, 1, 15, 8, 0),
            PunchTimeZoneId = "Not/AZone",
        };

        var response = await api.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"PunchTimeZoneId\"", body);
    }

    [Fact]
    public async Task Employee_CanResolveLocalTime()
    {
        // Employee is the floor for this endpoint's policy (matching /punches and /punches/batch) —
        // it never touches tenant data, so there's no reason to require Supervisor+.
        var (clientId, _) = await fixture.CreateClientAndScopedClientAsync($"Resolve Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");
        var request = new ResolveLocalPunchTimeRequest
        {
            PunchTime = LocalDateTime(2026, 6, 1, 13, 0),
            PunchTimeZoneId = "America/New_York",
        };

        var response = await employeeApi.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Instant> ResolveAsync(HttpClient api, LocalDateTime local, string zoneId, bool? daylightSaving)
    {
        var request = new ResolveLocalPunchTimeRequest { PunchTime = local, PunchTimeZoneId = zoneId, DaylightSaving = daylightSaving };
        var response = await api.PostAsJsonAsync("/punches/resolve-local-time", request, TestJson.Options, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var resolved = await response.Content.ReadFromJsonAsync<ResolveLocalPunchTimeResponse>(TestJson.Options, TestContext.Current.CancellationToken);
        return resolved!.PunchTime;
    }

    private static LocalDateTime LocalDateTime(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute);
}
