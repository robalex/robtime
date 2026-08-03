using NodaTime;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Hand-computed worked examples for <see cref="PayrollExportProjector"/> — pure, no DB, no
/// [Collection("Api")], matching DateRangeTests' precedent for a DB-free unit test in this project.
/// </summary>
public class PayrollExportProjectorTests
{
    private static readonly LocalDate PeriodStart = new(2026, 1, 1);
    private static readonly LocalDate PeriodEnd = new(2026, 1, 14);

    private static PayLineItem Line(
        PayLineType type, string code, decimal hours, decimal amount,
        decimal? baseRate = null, decimal? multiplier = null, LocalDate? shiftDate = null) => new()
    {
        Type = type,
        Code = code,
        Hours = hours,
        Amount = amount,
        BaseRate = baseRate,
        Multiplier = multiplier,
        ShiftDate = shiftDate ?? PeriodStart,
        AnchorPunchId = 1,
    };

    private static PayCalculationSnapshot Snapshot(int employeeId, params PayLineItem[] lines)
    {
        var byDate = lines.GroupBy(l => l.ShiftDate);
        var shifts = byDate.Select(g => new ShiftPay { ShiftDate = g.Key, AnchorPunchId = 1, LineItems = g.ToList() }).ToList();
        var week = new WorkweekPay { WeekStart = PeriodStart, Shifts = shifts };

        return new PayCalculationSnapshot
        {
            EmployeeId = employeeId,
            ClientId = 1,
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            Result = new PayResult { EmployeeId = employeeId, Workweeks = [week] },
        };
    }

    private static PayrollEarningCodeMapping Mapping(
        PayLineType type, string lineCode, string earningCode, PayrollExportValueBasis basis) => new()
    {
        ClientId = 1,
        ProfileId = 1,
        LineType = type,
        LineCode = lineCode,
        EarningCode = earningCode,
        ValueBasis = basis,
    };

    private static PayrollExportRounding Rounding(
        PayrollExportRoundingPolicy policy = PayrollExportRoundingPolicy.DistributeRemainder,
        string adjustmentCode = "") => new()
    {
        AmountScale = 2,
        HoursScale = 2,
        Policy = policy,
        AdjustmentEarningCode = adjustmentCode,
    };

    private static PayrollExportProjectionInput Input(
        IReadOnlyList<PayCalculationSnapshot> snapshots,
        IReadOnlyList<PayrollEarningCodeMapping> mappings,
        IReadOnlyDictionary<int, string>? externalIds = null,
        PayrollExportGrouping grouping = PayrollExportGrouping.PayPeriod,
        PayrollExportRounding? rounding = null) => new()
    {
        Snapshots = snapshots,
        Mappings = mappings,
        ExternalIdsByEmployeeId = externalIds
            ?? snapshots.Select(s => s.EmployeeId).Distinct().ToDictionary(id => id, id => $"EXT-{id}"),
        Grouping = grouping,
        Rounding = rounding ?? Rounding(),
    };

    [Fact]
    public void RegularHoursBasis_ExportsHoursAndRate()
    {
        var snapshot = Snapshot(1, Line(PayLineType.Regular, "", 8m, 160m, baseRate: 20m, multiplier: 1m));
        var mapping = Mapping(PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);

        var projection = PayrollExportProjector.Project(Input([snapshot], [mapping]));

        var row = Assert.Single(projection.Rows);
        Assert.Equal("REG", row.EarningCode);
        Assert.Equal(PayrollExportValueBasis.Hours, row.ValueBasis);
        Assert.Equal(8.00m, row.Hours);
        Assert.Equal(160.00m, row.Amount);
        Assert.Equal(20m, row.Rate);
        Assert.Equal(1, row.LineItemCount);
        Assert.True(projection.IsComplete);
    }

    [Fact]
    public void OvertimePremiumAmountBasis_ExportsAmountNotHours()
    {
        var snapshot = Snapshot(1, Line(PayLineType.OvertimePremium, "OVERTIME", 2m, 30m, baseRate: 15m, multiplier: 1m));
        var mapping = Mapping(PayLineType.OvertimePremium, "OVERTIME", "OT", PayrollExportValueBasis.Amount);

        var projection = PayrollExportProjector.Project(Input([snapshot], [mapping]));

        var row = Assert.Single(projection.Rows);
        Assert.Equal("OT", row.EarningCode);
        Assert.Equal(30.00m, row.Amount);
        Assert.Equal(15m, row.Rate);
    }

    [Fact]
    public void UnmappedLine_ProducesNoRow_AndMarksProjectionIncomplete()
    {
        var snapshot = Snapshot(1, Line(PayLineType.Differential, "NIGHT_SHIFT", 8m, 40m));

        var projection = PayrollExportProjector.Project(Input([snapshot], []));

        Assert.Empty(projection.Rows);
        var unmapped = Assert.Single(projection.UnmappedLines);
        Assert.Equal(PayLineType.Differential, unmapped.LineType);
        Assert.Equal("NIGHT_SHIFT", unmapped.LineCode);
        Assert.Equal(1, unmapped.LineItemCount);
        Assert.Equal(40m, unmapped.TotalAmount);
        Assert.Equal([1], unmapped.EmployeeIds);
        Assert.False(projection.IsComplete);
    }

    [Fact]
    public void EmployeeMissingExternalId_ProducesNoRows_ButReportsSnapshotGrossPay()
    {
        var snapshot = Snapshot(1, Line(PayLineType.Regular, "", 8m, 160m, baseRate: 20m));
        var mapping = Mapping(PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);
        var input = Input([snapshot], [mapping], externalIds: new Dictionary<int, string>());

        var projection = PayrollExportProjector.Project(input);

        Assert.Empty(projection.Rows);
        Assert.Equal([1], projection.EmployeesMissingExternalId);
        Assert.False(projection.IsComplete);

        var total = Assert.Single(projection.EmployeeTotals);
        Assert.Equal(160m, total.SnapshotGrossPay);
        Assert.Equal(0m, total.ExactRowTotal);
        Assert.Equal(0m, total.RoundedRowTotal);
    }

    [Fact]
    public void MultiRateWeek_CollapsesIntoOneRow_WithAmbiguousRate()
    {
        // Two Regular lines, same (empty) LineCode, different BaseRate -- a real multi-rate week
        // collapsing into one Hours-basis row under PayPeriod grouping. Rate must be null: reporting
        // either rate alone would misrepresent the other hours.
        var snapshot = Snapshot(
            1,
            Line(PayLineType.Regular, "", 8m, 160m, baseRate: 20m),
            Line(PayLineType.Regular, "", 2m, 50m, baseRate: 25m));
        var mapping = Mapping(PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);

        var projection = PayrollExportProjector.Project(Input([snapshot], [mapping]));

        var row = Assert.Single(projection.Rows);
        Assert.Equal(10m, row.ExactHours);
        Assert.Equal(210m, row.ExactAmount);
        Assert.Equal(2, row.LineItemCount);
        Assert.Null(row.Rate);
    }

    [Fact]
    public void PairStraddlingOvertimeAndDoubletime_ProducesTwoDistinctRows()
    {
        var snapshot = Snapshot(
            1,
            Line(PayLineType.OvertimePremium, "OVERTIME", 4m, 60m, baseRate: 15m),
            Line(PayLineType.OvertimePremium, "DOUBLETIME", 1m, 20m, baseRate: 20m));
        var mappings = new[]
        {
            Mapping(PayLineType.OvertimePremium, "OVERTIME", "OT", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.OvertimePremium, "DOUBLETIME", "DT", PayrollExportValueBasis.Amount),
        };

        var projection = PayrollExportProjector.Project(Input([snapshot], mappings));

        Assert.Equal(2, projection.Rows.Count);
        Assert.Contains(projection.Rows, r => r.EarningCode == "OT" && r.Amount == 60.00m);
        Assert.Contains(projection.Rows, r => r.EarningCode == "DT" && r.Amount == 20.00m);
    }

    [Fact]
    public void EmptySnapshotList_ProducesEmptyProjection_AndIsVacuouslyComplete()
    {
        var projection = PayrollExportProjector.Project(Input([], []));

        Assert.Empty(projection.Rows);
        Assert.Empty(projection.UnmappedLines);
        Assert.Empty(projection.EmployeesMissingExternalId);
        Assert.Empty(projection.EmployeeTotals);
        Assert.True(projection.IsComplete);
    }

    [Fact]
    public void ZeroValueLine_StillProducesARow_NotSilentlyDropped()
    {
        var snapshot = Snapshot(1, Line(PayLineType.FixedHours, "", 0m, 0m, baseRate: 15m));
        var mapping = Mapping(PayLineType.FixedHours, "", "PTO", PayrollExportValueBasis.Hours);

        var projection = PayrollExportProjector.Project(Input([snapshot], [mapping]));

        var row = Assert.Single(projection.Rows);
        Assert.Equal(0.00m, row.Hours);
        Assert.Equal(0.00m, row.Amount);
        Assert.Equal(1, row.LineItemCount);
    }

    [Fact]
    public void DistributeRemainder_TiedFractionalCents_SettleDeterministically()
    {
        // Three Amount-basis rows, each 3.335 -- a fractional cent, forced deliberately. Floor-cents
        // are 333 each ($3.33), all tied at a 0.5 remainder. Exact total 10.005 rounds (half-away-
        // from-zero) to 10.01, two cents above the floor sum of 9.99, so exactly two of the three
        // rows receive the extra cent. Tied remainders break by stable insertion order (A, then B),
        // so C is the one left at its floor.
        var snapshot = Snapshot(
            1,
            Line(PayLineType.Differential, "DIFF_A", 0m, 3.335m),
            Line(PayLineType.Differential, "DIFF_B", 0m, 3.335m),
            Line(PayLineType.Differential, "DIFF_C", 0m, 3.335m));
        var mappings = new[]
        {
            Mapping(PayLineType.Differential, "DIFF_A", "EC_A", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_B", "EC_B", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_C", "EC_C", PayrollExportValueBasis.Amount),
        };

        var projection = PayrollExportProjector.Project(Input([snapshot], mappings));

        var byCode = projection.Rows.ToDictionary(r => r.EarningCode);
        Assert.Equal(3.34m, byCode["EC_A"].Amount);
        Assert.True(byCode["EC_A"].IsRoundingAdjusted);
        Assert.Equal(3.34m, byCode["EC_B"].Amount);
        Assert.True(byCode["EC_B"].IsRoundingAdjusted);
        Assert.Equal(3.33m, byCode["EC_C"].Amount);
        Assert.False(byCode["EC_C"].IsRoundingAdjusted);

        var total = Assert.Single(projection.EmployeeTotals);
        Assert.Equal(10.005m, total.ExactRowTotal);
        Assert.Equal(10.01m, total.RoundedRowTotal);
        Assert.Equal(0.005m, total.RoundingResidual);
    }

    [Fact]
    public void DistributeRemainder_DistinctFractionalCents_PrefersLargestRemainderFirst()
    {
        // Three Amount-basis lines with DISTINCT fractional-cent remainders -- unlike the tied-cent
        // test above, this is the one case that can tell "give the extra cent to the largest
        // remainder first" (correct) apart from a reversed "smallest remainder first" bug: both
        // hand out the same NUMBER of extra cents and land on the same total, so a total-only
        // assertion cannot distinguish them. Only checking exactly which row received which amount
        // can.
        //
        // X=2.219 (scaled 221.9, remainder .9), Y=2.223 (222.3, remainder .3), Z=2.225 (222.5,
        // remainder .5). Floor sum = 221+222+222 = 665 cents; exact sum 666.7 rounds to target 667;
        // extraUnits = 2, going to the two LARGEST remainders: X (.9) and Z (.5). Y (.3), smallest,
        // stays at its floor.
        var snapshot = Snapshot(
            1,
            Line(PayLineType.Differential, "DIFF_X", 0m, 2.219m),
            Line(PayLineType.Differential, "DIFF_Y", 0m, 2.223m),
            Line(PayLineType.Differential, "DIFF_Z", 0m, 2.225m));
        var mappings = new[]
        {
            Mapping(PayLineType.Differential, "DIFF_X", "EC_X", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_Y", "EC_Y", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_Z", "EC_Z", PayrollExportValueBasis.Amount),
        };

        var projection = PayrollExportProjector.Project(Input([snapshot], mappings));

        var byCode = projection.Rows.ToDictionary(r => r.EarningCode);
        Assert.Equal(2.22m, byCode["EC_X"].Amount);
        Assert.True(byCode["EC_X"].IsRoundingAdjusted);
        Assert.Equal(2.22m, byCode["EC_Y"].Amount);
        Assert.False(byCode["EC_Y"].IsRoundingAdjusted);
        Assert.Equal(2.23m, byCode["EC_Z"].Amount);
        Assert.True(byCode["EC_Z"].IsRoundingAdjusted);
    }

    [Fact]
    public void AdjustmentRowPolicy_PostsResidualToDedicatedCode_IncludingNegative()
    {
        // Same three 3.335 lines. Independently nearest-rounded, each becomes 3.34 (10.02 total).
        // The exact total rounds to 10.01, one cent BELOW that independently-rounded sum -- proving
        // the adjustment row handles a negative residual, unlike DistributeRemainder's guaranteed
        // non-negative extraUnits (a genuinely different code path, not just a sign flip of the same
        // one).
        var snapshot = Snapshot(
            1,
            Line(PayLineType.Differential, "DIFF_A", 0m, 3.335m),
            Line(PayLineType.Differential, "DIFF_B", 0m, 3.335m),
            Line(PayLineType.Differential, "DIFF_C", 0m, 3.335m));
        var mappings = new[]
        {
            Mapping(PayLineType.Differential, "DIFF_A", "EC_A", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_B", "EC_B", PayrollExportValueBasis.Amount),
            Mapping(PayLineType.Differential, "DIFF_C", "EC_C", PayrollExportValueBasis.Amount),
        };
        var rounding = Rounding(PayrollExportRoundingPolicy.AdjustmentRow, adjustmentCode: "ADJ");

        var projection = PayrollExportProjector.Project(Input([snapshot], mappings, rounding: rounding));

        Assert.Equal(4, projection.Rows.Count);   // 3 real rows + 1 adjustment row
        Assert.All(
            projection.Rows.Where(r => r.EarningCode != "ADJ"),
            r => Assert.Equal(3.34m, r.Amount));

        var adjustment = Assert.Single(projection.Rows, r => r.EarningCode == "ADJ");
        Assert.Equal(-0.01m, adjustment.Amount);
        Assert.Equal(-0.01m, adjustment.ExactAmount);
        Assert.Equal(0, adjustment.LineItemCount);
        Assert.True(adjustment.IsRoundingAdjusted);

        var total = Assert.Single(projection.EmployeeTotals);
        Assert.Equal(10.01m, total.RoundedRowTotal);
    }

    [Fact]
    public void WorkDateGrouping_SplitsOneEarningCodeIntoOneRowPerDate()
    {
        var day1 = new LocalDate(2026, 1, 1);
        var day2 = new LocalDate(2026, 1, 2);
        var snapshot = Snapshot(
            1,
            Line(PayLineType.Regular, "", 8m, 160m, baseRate: 20m, shiftDate: day1),
            Line(PayLineType.Regular, "", 6m, 120m, baseRate: 20m, shiftDate: day2));
        var mapping = Mapping(PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours);

        var projection = PayrollExportProjector.Project(
            Input([snapshot], [mapping], grouping: PayrollExportGrouping.WorkDate));

        Assert.Equal(2, projection.Rows.Count);
        Assert.Contains(projection.Rows, r => r.WorkDate == day1 && r.Amount == 160.00m);
        Assert.Contains(projection.Rows, r => r.WorkDate == day2 && r.Amount == 120.00m);
    }
}
