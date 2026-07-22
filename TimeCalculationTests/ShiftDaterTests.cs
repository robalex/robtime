using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.ShiftBuilding;
using Xunit;

namespace TimeCalculationTests;

public class ShiftDaterTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private Shift MakeShift(Instant inTime, Instant outTime)
    {
        var inP  = TestEntityCreator.CreateTestPunch(inTime,  PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(outTime, PunchKind.Out, _emp);
        return new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(inP, outP)] };
    }

    [Fact]
    public void FirstPunchLocalDate_Strategy_UsesInPunchDate()
    {
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 9, 0), Instant.FromUtc(2023, 1, 2, 17, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void OvernightShift_WithFirstPunchStrategy_UsesStartDate()
    {
        // Starts Mon Jan 2 22:00, ends Tue Jan 3 06:00
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void LastPunchLocalDate_Strategy_UsesOutPunchDate()
    {
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.LastPunchLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 9, 0), Instant.FromUtc(2023, 1, 2, 17, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void OvernightShift_WithLastPunchStrategy_UsesEndDate()
    {
        // Starts Mon Jan 2 22:00, ends Tue Jan 3 06:00 — last punch lands on Jan 3
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.LastPunchLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 3), result[0].ShiftDate);
    }

    [Fact]
    public void LastPunchLocalDate_IncompleteShift_FallsBackToInPunchDate()
    {
        // No Out punch — last punch is the In punch itself
        var rule  = new PayRule { ShiftDateStrategy = ShiftDateStrategy.LastPunchLocalDate };
        var ctx   = TestEntityCreator.CreateContext(rule);
        var inP   = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 9, 0), PunchKind.In, _emp);
        var shift = new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(inP, null)] };

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void OvernightShift_WithMajorityHoursStrategy_UsesMajorityDate()
    {
        // 22:00 → 06:00: 2 hrs on Jan 2, 6 hrs on Jan 3 — majority on Jan 3
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.MajorityHoursLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 3), result[0].ShiftDate);
    }

    [Fact]
    public void DayShift_WithMajorityHoursStrategy_UsesCorrectDate()
    {
        // 09:00 → 17:00 entirely on Jan 2 — majority on Jan 2
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.MajorityHoursLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 9, 0), Instant.FromUtc(2023, 1, 2, 17, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void FirstPunchLocalDate_OrphanOutPair_NoInPunch_DoesNotCrash()
    {
        // Regression: AssignDate used to unconditionally dereference p.InPunch while ordering
        // pairs, throwing a NullReferenceException for an orphan Out (Out with no In).
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 17, 0), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(null, outP)] };

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void MajorityHoursLocalDate_ShiftWithOnlyOrphanPairs_FallsBackWithoutCrashing()
    {
        // Regression: GetMajorityHoursDate's filter checked only OutPunch != null (true for an
        // orphan Out too), then dereferenced InPunch unconditionally inside the loop.
        var rule = new PayRule { ShiftDateStrategy = ShiftDateStrategy.MajorityHoursLocalDate };
        var ctx  = TestEntityCreator.CreateContext(rule);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 17, 0), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(null, outP)] };

        var result = ShiftDater.AssignDatesToShifts([shift], ctx);

        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var ctx    = TestEntityCreator.CreateContext();
        var result = ShiftDater.AssignDatesToShifts([], ctx);
        Assert.Empty(result);
    }

    // ── SplitAtMidnight ──

    private static PipelineContext SplitCtx()
        => TestEntityCreator.CreateContext(new PayRule { ShiftDateStrategy = ShiftDateStrategy.SplitAtMidnight });

    [Fact]
    public void SplitAtMidnight_OvernightShift_BecomesTwoShiftsDatedToTheirOwnDays()
    {
        // 22:00 Jan 2 → 06:00 Jan 3 splits into 2 hrs on Jan 2 and 6 hrs on Jan 3.
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Equal(2, result.Count);
        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
        Assert.Equal(new LocalDate(2023, 1, 3), result[1].ShiftDate);
        Assert.Equal(2m, result[0].TotalHours);
        Assert.Equal(6m, result[1].TotalHours);
    }

    [Fact]
    public void SplitAtMidnight_PreservesTotalHours()
    {
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 3, 6, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Equal(shift.TotalHours, result.Sum(s => s.TotalHours));
    }

    [Fact]
    public void SplitAtMidnight_ShiftWithinOneDay_IsNotSplit()
    {
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 9, 0), Instant.FromUtc(2023, 1, 2, 17, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Single(result);
        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
        Assert.Equal(8m, result[0].TotalHours);
    }

    [Fact]
    public void SplitAtMidnight_SpanningThreeDays_ProducesAShiftPerDay()
    {
        // 22:00 Jan 2 → 02:00 Jan 4: 2h on Jan 2, 24h on Jan 3, 2h on Jan 4.
        var shift = MakeShift(Instant.FromUtc(2023, 1, 2, 22, 0), Instant.FromUtc(2023, 1, 4, 2, 0));

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Equal(3, result.Count);
        Assert.Equal([2m, 24m, 2m], result.Select(s => s.TotalHours));
    }

    [Fact]
    public void SplitAtMidnight_BoundaryPunchesAreSynthetic_OuterEdgesKeepOriginalIds()
    {
        // The real In/Out keep their ids at the outer edges; the midnight boundary punches are
        // synthetic copies (Id = 0) so they never collide with real punch identities.
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 22, 0), PunchKind.In, _emp, punchId: 11);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 6, 0), PunchKind.Out, _emp, punchId: 22);
        var shift = new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(inP, outP)] };

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Equal(11, result[0].PunchPairs[0].InPunch!.Id);   // real In
        Assert.Equal(0, result[0].PunchPairs[0].OutPunch!.Id);   // synthetic midnight
        Assert.Equal(0, result[1].PunchPairs[0].InPunch!.Id);    // synthetic midnight
        Assert.Equal(22, result[1].PunchPairs[0].OutPunch!.Id);  // real Out
    }

    [Fact]
    public void SplitAtMidnight_PreservesPositionAndRateOnEachPiece()
    {
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 22, 0), PunchKind.In, _emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 3, 6, 0), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [new PunchPair { InPunch = inP, OutPunch = outP, Rate = 25m }] };

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.All(result, s => Assert.Equal(25m, s.PunchPairs[0].Rate));
    }

    [Fact]
    public void SplitAtMidnight_OrphanPair_IsNotSplit_AndKeepsItsOwnDate()
    {
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 17, 0), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(null, outP)] };

        var result = ShiftDater.AssignDatesToShifts([shift], SplitCtx());

        Assert.Single(result);
        Assert.Equal(new LocalDate(2023, 1, 2), result[0].ShiftDate);
    }
}
