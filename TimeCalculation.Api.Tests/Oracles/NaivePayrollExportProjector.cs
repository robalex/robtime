using NodaTime;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Tests.Oracles;

/// <summary>
/// A deliberately naive second implementation of <see cref="PayrollExportProjector"/>'s exact-value
/// grouping, used as a test oracle (differential / N-version testing — same rationale as
/// TimeCalculationTests/Oracles/NaivePayPeriodCalculator.cs).
///
/// The real projector accumulates into a Dictionary&lt;RowGroupKey, RowAccumulator&gt; while reading
/// PayResult.LineItems once per snapshot as a flat list. This walks Workweeks → Shifts → LineItems by
/// hand and groups everything with plain LINQ GroupBy at the very end — same data, structurally
/// unrelated code path, so the two are unlikely to share a grouping-key bug.
///
/// Deliberately out of scope: rounding, residual distribution, and the AdjustmentRow policy. Those
/// are compared against hand-computed values in PayrollExportProjectorTests and asserted as
/// properties in PayrollExportProjectorPropertyTests instead — a naive version of THAT logic would
/// just be a second copy of the real algorithm, not an independently-reasoned check. This oracle
/// only proves the exact, pre-rounding grouping is right.
/// </summary>
internal static class NaivePayrollExportProjector
{
    internal static NaiveProjectionResult Project(PayrollExportProjectionInput input)
    {
        var mappedLines = new List<TaggedLine>();
        var unmappedLines = new List<TaggedUnmappedLine>();
        var missingExternalId = new List<int>();

        foreach (var snapshot in input.Snapshots)
        {
            var hasExternalId = input.ExternalIdsByEmployeeId.ContainsKey(snapshot.EmployeeId);
            if (!hasExternalId)
            {
                missingExternalId.Add(snapshot.EmployeeId);
            }

            foreach (var week in snapshot.Result.Workweeks)
            {
                foreach (var shift in week.Shifts)
                {
                    foreach (var line in shift.LineItems)
                    {
                        var mapping = FindMapping(input.Mappings, line.Type, line.Code);

                        if (mapping is null)
                        {
                            unmappedLines.Add(new TaggedUnmappedLine(line.Type, line.Code, line.Amount, snapshot.EmployeeId));
                        }
                        else if (hasExternalId)
                        {
                            var workDate = input.Grouping == PayrollExportGrouping.WorkDate ? line.ShiftDate : (LocalDate?)null;
                            mappedLines.Add(new TaggedLine(snapshot.EmployeeId, mapping.EarningCode, workDate, line));
                        }
                    }
                }
            }
        }

        var rows = mappedLines
            .GroupBy(t => new NaiveRowKey(t.EmployeeId, t.EarningCode, t.WorkDate))
            .Select(g => new NaiveExportRow
            {
                EmployeeId = g.Key.EmployeeId,
                EarningCode = g.Key.EarningCode,
                WorkDate = g.Key.WorkDate,
                ExactHours = g.Sum(t => t.Line.Hours),
                ExactAmount = g.Sum(t => t.Line.Amount),
                LineItemCount = g.Count(),
            })
            .ToList();

        var unmapped = unmappedLines
            .GroupBy(u => new NaiveLineKey(u.LineType, u.LineCode))
            .Select(g => new NaiveUnmappedLine
            {
                LineType = g.Key.LineType,
                LineCode = g.Key.LineCode,
                LineItemCount = g.Count(),
                TotalAmount = g.Sum(u => u.Amount),
                EmployeeIds = g.Select(u => u.EmployeeId).Distinct().OrderBy(id => id).ToList(),
            })
            .ToList();

        return new NaiveProjectionResult(rows, unmapped, missingExternalId.Distinct().OrderBy(id => id).ToList());
    }

    private static PayrollEarningCodeMapping? FindMapping(
        IReadOnlyList<PayrollEarningCodeMapping> mappings, PayLineType lineType, string lineCode)
    {
        PayrollEarningCodeMapping? found = null;
        foreach (var mapping in mappings)
        {
            if (mapping.LineType == lineType && mapping.LineCode == lineCode)
            {
                found = mapping;
                break;
            }
        }

        return found;
    }

    private sealed record TaggedLine(int EmployeeId, string EarningCode, LocalDate? WorkDate, PayLineItem Line);

    private sealed record TaggedUnmappedLine(PayLineType LineType, string LineCode, decimal Amount, int EmployeeId);

    private sealed record NaiveLineKey(PayLineType LineType, string LineCode);

    private sealed record NaiveRowKey(int EmployeeId, string EarningCode, LocalDate? WorkDate);
}

internal sealed record NaiveExportRow
{
    internal required int EmployeeId { get; init; }
    internal required string EarningCode { get; init; }
    internal required LocalDate? WorkDate { get; init; }
    internal required decimal ExactHours { get; init; }
    internal required decimal ExactAmount { get; init; }
    internal required int LineItemCount { get; init; }
}

internal sealed record NaiveUnmappedLine
{
    internal required PayLineType LineType { get; init; }
    internal required string LineCode { get; init; }
    internal required int LineItemCount { get; init; }
    internal required decimal TotalAmount { get; init; }
    internal required IReadOnlyList<int> EmployeeIds { get; init; }
}

internal sealed record NaiveProjectionResult(
    List<NaiveExportRow> Rows, List<NaiveUnmappedLine> Unmapped, List<int> MissingExternalId);
