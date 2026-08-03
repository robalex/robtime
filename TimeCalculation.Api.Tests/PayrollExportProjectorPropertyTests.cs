using NodaTime;
using TimeCalculation.Api.Services;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Metamorphic properties for <see cref="PayrollExportProjector"/>, run over snapshots produced by
/// the REAL engine (PayCalculator.Calculate), not hand-built PayCalculationSnapshot records — hand
/// built data would only test the projector against our own idea of a PayResult; engine-generated
/// data tests it against multi-rate weeks and tier-straddling pairs as they actually occur. Running
/// PayCalculator here is test-only and does not touch the sanctioned-callers guard in
/// TimeCalculation.Api.csproj, which governs production code, not test fixtures.
///
/// Every generated week uses a plain PayRule with no premiums/differentials/bonuses active — same
/// simplification PayCalculatorPropertyTests makes — so the only PayLineTypes ever produced are
/// Regular and OvertimePremium (OVERTIME/DOUBLETIME). AllLineKeysMapped() covers exactly those three
/// keys, which is what makes a mapped-fully test genuinely complete rather than accidentally so.
/// </summary>
public class PayrollExportProjectorPropertyTests
{
    private const decimal Tolerance = 0.0000001m;
    private static readonly LocalDate PeriodStart = new(2026, 1, 4);   // a Sunday
    private static readonly LocalDate PeriodEnd = new(2026, 1, 17);    // two full workweeks

    internal static Employee Employee(int id, decimal minimumWage = 15m) =>
        new() { Id = id, HomeTimeZoneId = "UTC", MinimumWage = minimumWage };

    internal static Punch CreatePunch(Instant time, PunchKind kind, Employee employee, int id, int? positionId = null) => new()
    {
        Id = id,
        PunchTime = time,
        Kind = kind,
        EmployeeId = employee.Id,
        Employee = employee,
        PositionId = positionId,
        PunchTimeZoneId = "UTC",
        CreatedAt = time,
        CreatedBy = "test",
    };

    internal static PipelineContext Context(Employee employee, int positionId, decimal baseRate) =>
        new(
            employee,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [new EmployeePositionAssignment(new Position { Id = positionId, BaseRate = baseRate }, new LocalDate(2000, 1, 1))]);

    /// <summary>Two concurrently-active positions at rates chosen so their weighted average rarely
    /// terminates in two decimal places (RegularRateCalculator's own weighted-average arithmetic is
    /// the natural source of genuine sub-cent precision — a single-rate week can never produce it,
    /// since hours × one clean rate always lands on whole cents). Every GenerateMultiRatePunches
    /// punch is tagged with an explicit PositionId; with two concurrent assignments and no override,
    /// resolution is ambiguous (a lesson this session hit once already), so tagging isn't optional.</summary>
    internal static PipelineContext MultiRateContext(Employee employee) =>
        new(
            employee,
            [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))],
            [
                new EmployeePositionAssignment(new Position { Id = 1, BaseRate = 19.37m }, new LocalDate(2000, 1, 1)),
                new EmployeePositionAssignment(new Position { Id = 2, BaseRate = 23.11m }, new LocalDate(2000, 1, 1)),
            ]);

    /// <summary>A two-week span of In/Out pairs on the quarter-hour grid, occasionally long enough to
    /// trip federal weekly overtime — same generation shape as PayCalculatorPropertyTests.</summary>
    internal static List<Punch> GeneratePunches(Random rng, Employee employee, int idBase, bool multiRate = false)
    {
        var punches = new List<Punch>();
        var nextId = idBase;

        for (var d = 0; d < 12; d++)
        {
            if (rng.NextDouble() < 0.15)
            {
                continue;
            }

            var positionId = multiRate ? rng.Next(2) + 1 : (int?)null;
            var startHour = rng.Next(5, 12);
            var length = rng.Next(2, 11);
            var start = PeriodStart.AtMidnight().InUtc().ToInstant() + Duration.FromDays(d) + Duration.FromHours(startHour);

            punches.Add(CreatePunch(start, PunchKind.In, employee, nextId++, positionId));
            punches.Add(CreatePunch(start + Duration.FromHours(length), PunchKind.Out, employee, nextId++, positionId));
        }

        return punches;
    }

    internal static PayCalculationSnapshot RunSnapshot(Employee employee, List<Punch> punches, PipelineContext ctx) => new()
    {
        EmployeeId = employee.Id,
        ClientId = 1,
        PeriodStart = PeriodStart,
        PeriodEnd = PeriodEnd,
        Result = PayCalculator.Calculate(punches, ctx),
    };

    internal static PayrollEarningCodeMapping Mapping(PayLineType type, string lineCode, string earningCode, PayrollExportValueBasis basis) =>
        new() { ClientId = 1, ProfileId = 1, LineType = type, LineCode = lineCode, EarningCode = earningCode, ValueBasis = basis };

    /// <summary>Every (LineType, LineCode) a plain-PayRule engine run can ever produce — see the class
    /// doc comment. Genuinely exhaustive for this generator, not merely "enough for these examples."</summary>
    internal static List<PayrollEarningCodeMapping> AllLineKeysMapped() =>
    [
        Mapping(PayLineType.Regular, "", "REG", PayrollExportValueBasis.Hours),
        Mapping(PayLineType.OvertimePremium, "OVERTIME", "OT", PayrollExportValueBasis.Amount),
        Mapping(PayLineType.OvertimePremium, "DOUBLETIME", "DT", PayrollExportValueBasis.Amount),
    ];

    internal static PayrollExportRounding Rounding(int amountScale = 2) => new()
    {
        AmountScale = amountScale,
        HoursScale = 2,
        Policy = PayrollExportRoundingPolicy.DistributeRemainder,
        AdjustmentEarningCode = "",
    };

    internal static PayrollExportProjectionInput Input(
        IReadOnlyList<PayCalculationSnapshot> snapshots, IReadOnlyList<PayrollEarningCodeMapping> mappings,
        PayrollExportGrouping grouping = PayrollExportGrouping.PayPeriod, PayrollExportRounding? rounding = null) => new()
    {
        Snapshots = snapshots,
        Mappings = mappings,
        ExternalIdsByEmployeeId = snapshots.Select(s => s.EmployeeId).Distinct().ToDictionary(id => id, id => $"EXT-{id}"),
        Grouping = grouping,
        Rounding = rounding ?? Rounding(),
    };

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void FullyMapped_Conserves_AndPartitionsEveryLineItem(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < 30; i++)
        {
            var employee = Employee(1);
            var punches = GeneratePunches(rng, employee, idBase: 1);
            var snapshot = RunSnapshot(employee, punches, Context(employee, positionId: 1, baseRate: 20m));

            var projection = PayrollExportProjector.Project(Input([snapshot], AllLineKeysMapped()));

            Assert.True(projection.IsComplete);

            // Conservation: every dollar of GrossPay is accounted for by exactly the rows built from
            // it — not "close to," exactly, because both sides trace back to the same unrounded sum.
            var exactTotal = projection.Rows.Sum(r => r.ExactAmount);
            AssertClose(snapshot.Result.GrossPay, exactTotal, $"seed {seed} iter {i}: gross vs row exact total");

            // Partition: this is the property conservation alone cannot catch -- two lines could be
            // dropped and duplicated in a way that cancels in the sum but not in the count.
            var lineItemCount = projection.Rows.Sum(r => r.LineItemCount);
            Assert.Equal(snapshot.Result.LineItems.Count, lineItemCount);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void MultiRateWeeks_StillConserve_UnderGenuineSubCentRegularRates(int seed)
    {
        // Same conservation property as FullyMapped_Conserves_AndPartitionsEveryLineItem, but over
        // MultiRateContext/multiRate:true data specifically, so a rounding-discipline bug (round
        // each line before summing, instead of summing first) has real fractional-cent input to
        // trip on. The single-rate generator used everywhere else in this file cannot produce that:
        // whole hours times one clean rate always lands on whole cents, so a round-then-aggregate
        // bug is invisible to it regardless of how many seeds or iterations are thrown at it.
        var rng = new Random(seed);

        for (var i = 0; i < 30; i++)
        {
            var employee = Employee(1);
            var punches = GeneratePunches(rng, employee, idBase: 1, multiRate: true);
            var snapshot = RunSnapshot(employee, punches, MultiRateContext(employee));

            var projection = PayrollExportProjector.Project(Input([snapshot], AllLineKeysMapped()));

            Assert.True(projection.IsComplete);
            var exactTotal = projection.Rows.Sum(r => r.ExactAmount);
            AssertClose(snapshot.Result.GrossPay, exactTotal, $"seed {seed} iter {i}: gross vs row exact total (multi-rate)");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)]
    public void RoundedTotal_MatchesRoundedExactTotal_AndNoRowDistortedByMoreThanOneUnit(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < 20; i++)
        {
            var employee = Employee(1);
            var punches = GeneratePunches(rng, employee, idBase: 1);
            var snapshot = RunSnapshot(employee, punches, Context(employee, positionId: 1, baseRate: 20m));
            var scale = rng.Next(0, 5);

            var projection = PayrollExportProjector.Project(
                Input([snapshot], AllLineKeysMapped(), rounding: Rounding(scale)));

            var total = Assert.Single(projection.EmployeeTotals);
            var expectedRounded = Math.Round(total.ExactRowTotal, scale, MidpointRounding.AwayFromZero);
            Assert.Equal(expectedRounded, total.RoundedRowTotal);

            var unit = 1m;
            for (var s = 0; s < scale; s++)
            {
                unit /= 10m;
            }

            foreach (var row in projection.Rows)
            {
                Assert.True(
                    Math.Abs(row.Amount - row.ExactAmount) <= unit + Tolerance,
                    $"seed {seed}: {row.EarningCode} distorted by more than one unit at scale {scale}.");
            }
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)]
    public void WorkDateGrouping_ResummedByEarningCode_MatchesPayPeriodGrouping_Exactly(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < 20; i++)
        {
            var employee = Employee(1);
            var punches = GeneratePunches(rng, employee, idBase: 1);
            var snapshot = RunSnapshot(employee, punches, Context(employee, positionId: 1, baseRate: 20m));

            var byPeriod = PayrollExportProjector.Project(
                Input([snapshot], AllLineKeysMapped(), PayrollExportGrouping.PayPeriod));
            var byWorkDate = PayrollExportProjector.Project(
                Input([snapshot], AllLineKeysMapped(), PayrollExportGrouping.WorkDate));

            var resummed = byWorkDate.Rows
                .GroupBy(r => r.EarningCode)
                .ToDictionary(g => g.Key, g => (Hours: g.Sum(r => r.ExactHours), Amount: g.Sum(r => r.ExactAmount), Count: g.Sum(r => r.LineItemCount)));

            Assert.Equal(byPeriod.Rows.Count, resummed.Count);

            foreach (var row in byPeriod.Rows)
            {
                var (hours, amount, count) = resummed[row.EarningCode];
                AssertClose(row.ExactHours, hours, $"seed {seed}: {row.EarningCode} exact hours across groupings");
                AssertClose(row.ExactAmount, amount, $"seed {seed}: {row.EarningCode} exact amount across groupings");
                Assert.Equal(row.LineItemCount, count);
            }
        }
    }

    [Fact]
    public void RemovingAMatchedMapping_FlipsIncomplete_AndReportsExactlyTheRemovedAmount()
    {
        var rng = new Random(7);
        var employee = Employee(1);
        var punches = GeneratePunches(rng, employee, idBase: 1);
        var snapshot = RunSnapshot(employee, punches, Context(employee, positionId: 1, baseRate: 20m));

        var full = PayrollExportProjector.Project(Input([snapshot], AllLineKeysMapped()));
        Assert.True(full.IsComplete);
        var regularRow = full.Rows.Single(r => r.EarningCode == "REG");

        var withoutRegular = AllLineKeysMapped().Where(m => m.LineCode != "" || m.LineType != PayLineType.Regular).ToList();
        var degraded = PayrollExportProjector.Project(Input([snapshot], withoutRegular));

        Assert.False(degraded.IsComplete);
        var unmapped = Assert.Single(degraded.UnmappedLines);
        Assert.Equal(PayLineType.Regular, unmapped.LineType);
        Assert.Equal("", unmapped.LineCode);
        AssertClose(regularRow.ExactAmount, unmapped.TotalAmount, "removed mapping's total");
        Assert.Equal(regularRow.LineItemCount, unmapped.LineItemCount);
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)]
    public void Project_IsDeterministic_AcrossRepeatedRunsOfEqualInput(int seed)
    {
        var rng = new Random(seed);
        var employee = Employee(1);
        var punches = GeneratePunches(rng, employee, idBase: 1);
        var snapshot = RunSnapshot(employee, punches, Context(employee, positionId: 1, baseRate: 20m));

        // Deliberately distinct list instances holding equal-value snapshots/mappings, so a bug
        // that (wrongly) relied on reference identity anywhere couldn't hide behind reusing the
        // same objects across both calls.
        var first = PayrollExportProjector.Project(Input([snapshot], AllLineKeysMapped()));
        var second = PayrollExportProjector.Project(Input([snapshot with { }], AllLineKeysMapped()));

        AssertProjectionsEqual(first, second);
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)]
    public void Project_IsInvariantToInputOrder_OfSnapshotsAndMappings(int seed)
    {
        var rng = new Random(seed);
        var employeeA = Employee(1);
        var employeeB = Employee(2);
        var snapshotA = RunSnapshot(employeeA, GeneratePunches(rng, employeeA, idBase: 1), Context(employeeA, positionId: 1, baseRate: 20m));
        var snapshotB = RunSnapshot(employeeB, GeneratePunches(rng, employeeB, idBase: 1000), Context(employeeB, positionId: 2, baseRate: 25m));
        var mappings = AllLineKeysMapped();

        var forward = PayrollExportProjector.Project(Input([snapshotA, snapshotB], mappings));
        var reversed = PayrollExportProjector.Project(
            Input([snapshotB, snapshotA], mappings.AsEnumerable().Reverse().ToList()));

        AssertProjectionsEqual(forward, reversed);
    }

    [Theory]
    [InlineData(1)] [InlineData(42)]
    public void Project_IsAdditive_OverDisjointEmployees(int seed)
    {
        var rng = new Random(seed);
        var employeeA = Employee(1);
        var employeeB = Employee(2);
        var snapshotA = RunSnapshot(employeeA, GeneratePunches(rng, employeeA, idBase: 1), Context(employeeA, positionId: 1, baseRate: 20m));
        var snapshotB = RunSnapshot(employeeB, GeneratePunches(rng, employeeB, idBase: 1000), Context(employeeB, positionId: 2, baseRate: 25m));
        var mappings = AllLineKeysMapped();

        var combined = PayrollExportProjector.Project(Input([snapshotA, snapshotB], mappings));
        var separateA = PayrollExportProjector.Project(Input([snapshotA], mappings));
        var separateB = PayrollExportProjector.Project(Input([snapshotB], mappings));
        var separateRows = separateA.Rows.Concat(separateB.Rows)
            .OrderBy(r => r.EmployeeId).ThenBy(r => r.EarningCode, StringComparer.Ordinal).ToList();
        var combinedRows = combined.Rows
            .OrderBy(r => r.EmployeeId).ThenBy(r => r.EarningCode, StringComparer.Ordinal).ToList();

        Assert.Equal(separateRows, combinedRows);
    }

    /// <summary>Field-by-field, not record `==` — PayrollExportProjection (and UnmappedLine) hold
    /// IReadOnlyList properties, and synthesized record equality compares those by reference, so `==`
    /// across two independent Project() calls is always false regardless of content. Same trap this
    /// suite already hit once in PayCalculatorPropertyTests; PayrollExportRow itself is all scalars,
    /// so list-level Assert.Equal (element-wise) on Rows is safe and sufficient on its own.</summary>
    private static void AssertProjectionsEqual(PayrollExportProjection expected, PayrollExportProjection actual)
    {
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.EmployeeTotals, actual.EmployeeTotals);
        Assert.Equal(expected.EmployeesMissingExternalId, actual.EmployeesMissingExternalId);
        Assert.Equal(expected.UnmappedLines.Count, actual.UnmappedLines.Count);

        foreach (var (e, a) in expected.UnmappedLines.Zip(actual.UnmappedLines))
        {
            Assert.Equal(e.LineType, a.LineType);
            Assert.Equal(e.LineCode, a.LineCode);
            Assert.Equal(e.LineItemCount, a.LineItemCount);
            Assert.Equal(e.TotalAmount, a.TotalAmount);
            Assert.Equal(e.EmployeeIds, a.EmployeeIds);
        }
    }

    private static void AssertClose(decimal expected, decimal actual, string context) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{context}: expected {expected}, got {actual}.");
}
