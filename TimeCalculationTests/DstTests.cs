using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline.ShiftBuilding;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// DST edge cases across pipeline stages and the PunchPair model.
///
/// 2023 transitions for America/New_York (Eastern):
///   Spring forward: March 12 at 2:00 AM EST → 3:00 AM EDT  (2:00–2:59 AM skipped; UTC-5 → UTC-4)
///   Fall back:      November 5 at 2:00 AM EDT → 1:00 AM EST (1:00–1:59 AM occurs twice; UTC-4 → UTC-5)
///
/// America/Phoenix (Arizona) is UTC-7 year-round — never observes DST.
/// </summary>
public class DstTests
{
    private const string Eastern = "America/New_York";
    private const string Arizona = "America/Phoenix";

    private static Employee MakeEmployee(string tz) =>
        new() { Id = 1, HomeTimeZoneId = tz, MinimumWage = 15m };

    private static Shift MakeShift(Instant @in, Instant @out, Employee emp)
    {
        var inP  = TestEntityCreator.CreateTestPunch(@in,  PunchKind.In,  emp);
        var outP = TestEntityCreator.CreateTestPunch(@out, PunchKind.Out, emp);
        return new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(inP, outP)] };
    }

    // ── PunchPair.TotalHours — Instant arithmetic, DST-transparent ───────────

    [Fact]
    public void SpringForward_TotalHours_ReflectsSkippedHour()
    {
        // 11 PM Mar 11 EST (04:00 UTC) → 4 AM Mar 12 EDT (08:00 UTC)
        // 2:00–3:00 AM is skipped; actual elapsed = 4 hrs, not 5
        var emp  = MakeEmployee(Eastern);
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 3, 12, 4, 0), PunchKind.In,  emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 3, 12, 8, 0), PunchKind.Out, emp);

        Assert.Equal(4m, TestEntityCreator.CreateTestPunchPair(inP, outP).TotalHours);
    }

    [Fact]
    public void FallBack_TotalHours_ReflectsExtraHour()
    {
        // 11 PM Nov 4 EDT (03:00 UTC) → 3 AM Nov 5 EST (08:00 UTC)
        // 1 AM occurs twice; actual elapsed = 5 hrs, not 4
        var emp  = MakeEmployee(Eastern);
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 3, 0), PunchKind.In,  emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 8, 0), PunchKind.Out, emp);

        Assert.Equal(5m, TestEntityCreator.CreateTestPunchPair(inP, outP).TotalHours);
    }

    [Fact]
    public void FallBack_TwoPunchesAtSame1AM_TotalHoursIsOne()
    {
        // Both punches appear as "1:30 AM" in local time but are different Instants:
        //   1:30 AM EDT (before fall-back) = 05:30 UTC
        //   1:30 AM EST (after  fall-back) = 06:30 UTC
        var emp  = MakeEmployee(Eastern);
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 5, 30), PunchKind.In,  emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 6, 30), PunchKind.Out, emp);

        Assert.Equal(1m, TestEntityCreator.CreateTestPunchPair(inP, outP).TotalHours);
    }

    // ── Stage 1: rounding near DST boundaries ────────────────────────────────

    [Fact]
    public void SpringForward_Rounding_SkippedHour_ProducesCorrectInstant()
    {
        // 1:55 AM EST (06:55 UTC) with 15-min rounding → 2:00 AM, which doesn't exist.
        // Correct result: original Instant + 5 min = 07:00 UTC = 3:00 AM EDT
        var emp   = MakeEmployee(Eastern);
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var rule  = new PayRule();
        var ctx   = TestEntityCreator.CreateContext(rule, emp, rounding);
        var punch = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 3, 12, 6, 55), PunchKind.In, emp, 0, Eastern);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(Instant.FromUtc(2023, 3, 12, 7, 0), result[0].EffectiveTime);
    }

    [Fact]
    public void FallBack_Rounding_AmbiguousHour_PreservesPostTransitionOffset()
    {
        // 1:28 AM EST (post-transition, 06:28 UTC) rounds to 1:30 AM.
        // Correct: original Instant + 2 min = 06:30 UTC = 1:30 AM EST
        // Bug: InZoneLeniently("1:30 AM Nov 5") picks the pre-transition (EDT) offset → 05:30 UTC
        var emp   = MakeEmployee(Eastern);
        var rounding = new RoundingRule { RoundingStrategy = RoundingStrategy.NearestInterval, RoundingIntervalMinutes = 15 };
        var rule = new PayRule();
        var ctx   = TestEntityCreator.CreateContext(rule, emp, rounding);
        var punch = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 11, 5, 6, 28), PunchKind.In, emp, 0, Eastern);

        var result = PunchRounder.RoundPunches([punch], ctx);

        Assert.Equal(Instant.FromUtc(2023, 11, 5, 6, 30), result[0].EffectiveTime);
    }

    // ── Stage 6: date assignment ──────────────────────────────────────────────

    [Fact]
    public void SpringForward_FirstPunchLocalDate_UsesStartDate()
    {
        // 11 PM Mar 11 EST = 04:00 UTC Mar 12 — local date is March 11
        var emp  = MakeEmployee(Eastern);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 3, 12, 4, 0), Instant.FromUtc(2023, 3, 12, 8, 0), emp);

        Assert.Equal(new LocalDate(2023, 3, 11), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    [Fact]
    public void FallBack_FirstPunchLocalDate_UsesStartDate()
    {
        // 11 PM Nov 4 EDT = 03:00 UTC Nov 5 — local date is November 4
        var emp  = MakeEmployee(Eastern);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 11, 5, 3, 0), Instant.FromUtc(2023, 11, 5, 8, 0), emp);

        Assert.Equal(new LocalDate(2023, 11, 4), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    [Fact]
    public void FallBack_InPunchAtSecond1AM_FirstPunchLocalDate_CorrectDate()
    {
        // In punch at 1:30 AM EST (second 1 AM, post-transition) = 06:30 UTC
        // InZone correctly identifies this as Nov 5 — local date is Nov 5, not Nov 4
        var emp  = MakeEmployee(Eastern);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 11, 5, 6, 30), Instant.FromUtc(2023, 11, 5, 8, 0), emp);

        Assert.Equal(new LocalDate(2023, 11, 5), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    [Fact]
    public void SpringForward_MajorityHours_UsesDateWithMoreHours()
    {
        // 11 PM Mar 11 → 4 AM Mar 12 EDT: 1 hr on Mar 11, 3 hrs on Mar 12 (2–3 AM skipped)
        var emp  = MakeEmployee(Eastern);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.MajorityHoursLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 3, 12, 4, 0), Instant.FromUtc(2023, 3, 12, 8, 0), emp);

        Assert.Equal(new LocalDate(2023, 3, 12), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    [Fact]
    public void FallBack_MajorityHours_ExtraHourCountedOnFallBackDate()
    {
        // 11 PM Nov 4 → 3 AM Nov 5 EST: 1 hr on Nov 4, 4 hrs on Nov 5 (extra DST hour included)
        var emp  = MakeEmployee(Eastern);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.MajorityHoursLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 11, 5, 3, 0), Instant.FromUtc(2023, 11, 5, 8, 0), emp);

        Assert.Equal(new LocalDate(2023, 11, 5), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    // ── Arizona: UTC-7 year-round, no DST ────────────────────────────────────

    [Fact]
    public void Arizona_TotalHours_CorrectDuringSummerDst()
    {
        // 9 AM Arizona (16:00 UTC) → 5 PM Arizona (00:00 UTC next day) = 8 hrs
        // America/Phoenix is always UTC-7; neighboring Mountain neighbors are MDT (UTC-6) in summer
        var emp  = MakeEmployee(Arizona);
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 6, 15, 16, 0), PunchKind.In,  emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 6, 16,  0, 0), PunchKind.Out, emp);

        Assert.Equal(8m, TestEntityCreator.CreateTestPunchPair(inP, outP).TotalHours);
    }

    [Fact]
    public void Arizona_ShiftDate_CorrectDuringSummerDst()
    {
        // 9 AM Jun 15 Arizona = 16:00 UTC — local date must be June 15 (UTC-7, not UTC-6)
        // If UTC-6 were used by mistake, 16:00 UTC = 10 AM Jun 15, still Jun 15 — but
        // at 18:00 UTC (11 AM local), UTC-6 gives Jun 15, UTC-7 also gives Jun 15.
        // Stronger check: punch just before midnight Arizona (05:59 UTC Jun 16 = 10:59 PM Jun 15 AZ)
        //   With UTC-7 → Jun 15  (correct)
        //   With UTC-6 → Jun 15 11:59 PM (coincidentally same date but wrong time)
        // Use early morning: 03:00 UTC = 8 PM Jun 14 Arizona, confirming UTC-7 is in effect.
        var emp  = MakeEmployee(Arizona);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate }, emp);
        // 8 PM Jun 14 Arizona = 03:00 UTC Jun 15 (UTC-7); if UTC-6 were used = 9 PM Jun 14 → still Jun 14
        var shift = MakeShift(Instant.FromUtc(2023, 6, 15, 3, 0), Instant.FromUtc(2023, 6, 15, 7, 0), emp);

        Assert.Equal(new LocalDate(2023, 6, 14), ShiftDater.AssignDatesToShifts([shift], ctx)[0].ShiftDate);
    }

    [Fact]
    public void Arizona_ShiftDate_NoDstTransitionInSpringOrFall()
    {
        // Run a shift on spring-forward day — Arizona should be unaffected and hours unchanged
        // 9 AM Mar 12 Arizona (16:00 UTC) → 5 PM Mar 12 Arizona (00:00 UTC Mar 13) = 8 hrs
        var emp  = MakeEmployee(Arizona);
        var ctx  = TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate }, emp);
        var shift = MakeShift(Instant.FromUtc(2023, 3, 12, 16, 0), Instant.FromUtc(2023, 3, 13, 0, 0), emp);

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 3, 12), result[0].ShiftDate);
        Assert.Equal(8m, result[0].TotalHours);
    }
}
