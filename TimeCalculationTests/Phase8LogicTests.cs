using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Ingestion;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

public class IdempotentIngestTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private Punch WithKey(string? device, string? id, int hour = 9) =>
        TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, hour, 0), PunchKind.In, _emp)
            with { DeviceId = device, DevicePunchId = id };

    [Fact]
    public void DuplicateOfStoredPunch_IsDropped()
    {
        var existing = new[] { WithKey("dev1", "p100") };
        var incoming = new[] { WithKey("dev1", "p100"), WithKey("dev1", "p101") };

        var accepted = IdempotentIngest.Deduplicate(existing, incoming);
        Assert.Single(accepted);
        Assert.Equal("p101", accepted[0].DevicePunchId);
    }

    [Fact]
    public void DuplicateWithinBatch_IsDroppedOnce()
    {
        var incoming = new[] { WithKey("dev1", "p1"), WithKey("dev1", "p1", hour: 10) };
        var accepted = IdempotentIngest.Deduplicate([], incoming);
        Assert.Single(accepted);
    }

    [Fact]
    public void PunchesWithoutDeviceKey_AreNeverDeduped()
    {
        var incoming = new[] { WithKey(null, null), WithKey(null, null) };
        var accepted = IdempotentIngest.Deduplicate([], incoming);
        Assert.Equal(2, accepted.Count);
    }

    [Fact]
    public void SameDevicePunchId_DifferentEmployee_NotDuplicate()
    {
        var other = new Employee { Id = 2, HomeTimeZoneId = "UTC", MinimumWage = 15m };
        var a = WithKey("dev1", "p1");
        var b = WithKey("dev1", "p1") with { EmployeeId = other.Id };
        var accepted = IdempotentIngest.Deduplicate([], [a, b]);
        Assert.Equal(2, accepted.Count);
    }
}

public class RetroactiveBonusRecalculatorTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 20m };

    private Workweek WeekWith(decimal hours, LocalDate start)
    {
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 8, 0), PunchKind.In, _emp);
        var outP = TestEntityCreator.CreateTestPunch(
            Instant.FromUtc(2023, 1, 2, 8, 0) + Duration.FromHours((double)hours), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = 20m }] };
        var day = new WorkDay { Date = start.PlusDays(1), Shifts = [shift] };
        return new Workweek { StartDate = start, Days = [day] };
    }

    [Fact]
    public void MultiWeekBonus_ApportionedByHours_AndAddsOvertimePremium()
    {
        // Two weeks, both 50 hrs (federal → 10 OT each). $400 bonus over 100 hrs → $200/week.
        // New rate adds 200/50 = $4/hr; additional OT premium = 10 × 0.5 × 4 = $20 per week.
        var weeks = new[]
        {
            WeekWith(50m, new LocalDate(2023, 1, 1)),
            WeekWith(50m, new LocalDate(2023, 1, 8)),
        };

        var result = RetroactiveBonusRecalculator.Recalculate(400m, weeks, new FederalOvertimeRule());

        Assert.Equal(2, result.PerWeek.Count);
        Assert.All(result.PerWeek, w => Assert.Equal(200m, w.AllocatedBonus));
        Assert.All(result.PerWeek, w => Assert.Equal(20m, w.AdditionalOvertimePremium));
        Assert.Equal(40m, result.AdditionalOvertimePremium);
    }

    [Fact]
    public void BonusInWeekWithoutOvertime_AddsNoPremium()
    {
        var weeks = new[] { WeekWith(30m, new LocalDate(2023, 1, 1)) };
        var result = RetroactiveBonusRecalculator.Recalculate(300m, weeks, new FederalOvertimeRule());

        Assert.Equal(300m, result.PerWeek[0].AllocatedBonus);
        Assert.Equal(0m, result.AdditionalOvertimePremium);
    }
}
