using NodaTime;
using TimeCalculation.Api.Services;
using TimeCalculation.Api.Tests.Oracles;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// Differential test pitting <see cref="PayrollExportProjector"/> against
/// <see cref="NaivePayrollExportProjector"/> (see that class for the rationale). Reuses the
/// engine-backed generation helpers on <see cref="PayrollExportProjectorPropertyTests"/> so both
/// suites exercise the same realistic multi-rate/tier-straddling data, not two different toy sets.
/// </summary>
public class PayrollExportProjectorOracleTests
{
    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void MatchesNaiveOracle_OnExactGroupingAcrossFullyAndPartiallyMappedRuns(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < 30; i++)
        {
            var employeeA = PayrollExportProjectorPropertyTests.Employee(1);
            var employeeB = PayrollExportProjectorPropertyTests.Employee(2);
            var snapshotA = PayrollExportProjectorPropertyTests.RunSnapshot(
                employeeA, PayrollExportProjectorPropertyTests.GeneratePunches(rng, employeeA, idBase: 1),
                PayrollExportProjectorPropertyTests.Context(employeeA, positionId: 1, baseRate: 20m));
            var snapshotB = PayrollExportProjectorPropertyTests.RunSnapshot(
                employeeB, PayrollExportProjectorPropertyTests.GeneratePunches(rng, employeeB, idBase: 1000),
                PayrollExportProjectorPropertyTests.Context(employeeB, positionId: 2, baseRate: 25m));

            // Every third iteration drops the DOUBLETIME mapping, so the oracle is checked against
            // partially-mapped runs too, not only the fully-complete case.
            var mappings = PayrollExportProjectorPropertyTests.AllLineKeysMapped();
            if (i % 3 == 0)
            {
                mappings = mappings.Where(m => m.LineCode != "DOUBLETIME").ToList();
            }

            var grouping = i % 2 == 0 ? PayrollExportGrouping.PayPeriod : PayrollExportGrouping.WorkDate;
            var input = PayrollExportProjectorPropertyTests.Input([snapshotA, snapshotB], mappings, grouping);

            var actual = PayrollExportProjector.Project(input);
            var expected = NaivePayrollExportProjector.Project(input);

            AssertRowsMatch(expected.Rows, actual.Rows, seed, i);
            AssertUnmappedMatch(expected.Unmapped, actual.UnmappedLines, seed, i);
            Assert.Equal(expected.MissingExternalId, actual.EmployeesMissingExternalId);
        }
    }

    private sealed record RowMatchKey(int EmployeeId, string EarningCode, LocalDate? WorkDate);

    private sealed record UnmappedMatchKey(PayLineType LineType, string LineCode);

    private static void AssertRowsMatch(
        List<NaiveExportRow> expected, IReadOnlyList<PayrollExportRow> actual, int seed, int iteration)
    {
        var expectedByKey = expected
            .ToDictionary(r => new RowMatchKey(r.EmployeeId, r.EarningCode, r.WorkDate));
        var actualByKey = actual
            .ToDictionary(r => new RowMatchKey(r.EmployeeId, r.EarningCode, r.WorkDate));

        Assert.True(
            expectedByKey.Count == actualByKey.Count,
            $"seed {seed} iter {iteration}: oracle produced {expectedByKey.Count} row groups, projector produced {actualByKey.Count}.");

        foreach (var (key, oracleRow) in expectedByKey)
        {
            Assert.True(actualByKey.TryGetValue(key, out var realRow), $"seed {seed} iter {iteration}: missing row for {key}.");
            Assert.Equal(oracleRow.ExactHours, realRow!.ExactHours);
            Assert.Equal(oracleRow.ExactAmount, realRow.ExactAmount);
            Assert.Equal(oracleRow.LineItemCount, realRow.LineItemCount);
        }
    }

    private static void AssertUnmappedMatch(
        List<NaiveUnmappedLine> expected, IReadOnlyList<PayrollExportUnmappedLine> actual, int seed, int iteration)
    {
        var expectedByKey = expected.ToDictionary(u => new UnmappedMatchKey(u.LineType, u.LineCode));
        var actualByKey = actual.ToDictionary(u => new UnmappedMatchKey(u.LineType, u.LineCode));

        Assert.True(
            expectedByKey.Count == actualByKey.Count,
            $"seed {seed} iter {iteration}: oracle found {expectedByKey.Count} unmapped keys, projector found {actualByKey.Count}.");

        foreach (var (key, oracleUnmapped) in expectedByKey)
        {
            Assert.True(actualByKey.TryGetValue(key, out var realUnmapped), $"seed {seed} iter {iteration}: missing unmapped entry for {key}.");
            Assert.Equal(oracleUnmapped.LineItemCount, realUnmapped!.LineItemCount);
            Assert.Equal(oracleUnmapped.TotalAmount, realUnmapped.TotalAmount);
            Assert.Equal(oracleUnmapped.EmployeeIds, realUnmapped.EmployeeIds);
        }
    }
}
