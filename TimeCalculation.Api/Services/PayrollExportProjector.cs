using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Turns frozen <see cref="PayCalculationSnapshot"/>s into payroll export rows. Pure — no
/// PayrollDbContext, no IQueryable, no clock, no I/O — per CLAUDE.md's service/logic split, and
/// because export deliberately reads only the frozen snapshot (UI_PLAN.md decision 25: "payroll
/// export reads the snapshot, never recalculates"). This never touches PayCalculator; the four
/// sanctioned callers listed in TimeCalculation.Api.csproj's guard comment are unaffected.
///
/// Core discipline: aggregate every contributing PayLineItem in full precision first, round once at
/// the very end. Because PayResult.GrossPay is itself an unrounded sum of the same lines, a row
/// group's pre-rounding total equals its slice of GrossPay by construction — the reconciliation
/// invariant the property tests assert is not an accident of the arithmetic, it falls out of doing
/// the sum in the right order.
///
/// Rounding (DistributeRemainder, the default policy) uses the largest-remainder method: floor every
/// row to its own minor unit (a cent, at scale 2), then hand out the whole-unit gap between that
/// floor-sum and the nearest-rounded target to the rows with the largest fractional remainder, so
/// every row moves by at most one minor unit. This is guaranteed to land exactly on target and never
/// needs a negative branch: writing baseline_i = floor(x_i) and r_i = x_i - baseline_i (so r_i is in
/// [0, 1)), the gap is (target - Σbaseline_i) = Σr_i + (target - Σx_i), and target is within 0.5 of
/// Σx_i by definition of nearest-rounding, so the gap is always in [-0.5, Σr_i + 0.5) — since it must
/// also be a whole number and Σr_i ≥ 0, it is always in [0, rowCount]. There is never a shortfall to
/// cover by rounding a row down below its own floor.
/// </summary>
public static class PayrollExportProjector
{
    public static PayrollExportProjection Project(PayrollExportProjectionInput input)
    {
        var mappingsByKey = BuildMappingIndex(input.Mappings);
        var rowGroups = new Dictionary<RowGroupKey, RowAccumulator>();
        var unmappedByKey = new Dictionary<LineKey, UnmappedAccumulator>();
        var missingExternalId = new HashSet<int>();

        // Keyed by employee so a caller accidentally passing two snapshots for the same employee
        // (a single export run is expected to carry exactly one per employee) still produces a
        // well-defined total rather than silently dropping one of them.
        var snapshotGrossByEmployee = new Dictionary<int, decimal>();

        foreach (var snapshot in input.Snapshots)
        {
            snapshotGrossByEmployee[snapshot.EmployeeId] =
                snapshotGrossByEmployee.GetValueOrDefault(snapshot.EmployeeId) + snapshot.Result.GrossPay;

            var hasExternalId = input.ExternalIdsByEmployeeId.TryGetValue(snapshot.EmployeeId, out var externalId);
            if (!hasExternalId)
            {
                missingExternalId.Add(snapshot.EmployeeId);
            }

            // Read once — PayResult.LineItems, WorkweekPay.LineItems and every .Gross re-materialize
            // the flattened tree on each access, so looping the property directly would be an
            // accidental O(n^2) over the snapshot's own shifts.
            var lineItems = snapshot.Result.LineItems;

            foreach (var line in lineItems)
            {
                AccumulateLine(
                    line, snapshot, hasExternalId, externalId, input.Grouping,
                    mappingsByKey, rowGroups, unmappedByKey);
            }
        }

        var accumulatorsByEmployee = rowGroups.Values.ToLookup(a => a.EmployeeId);
        var allRows = new List<PayrollExportRow>();
        var employeeTotals = new List<PayrollExportEmployeeTotal>();

        foreach (var employeeId in snapshotGrossByEmployee.Keys.OrderBy(id => id))
        {
            var group = accumulatorsByEmployee[employeeId].ToList();
            var result = RoundEmployeeGroup(group, input.Rounding);

            allRows.AddRange(result.Rows);

            var providerPriced = group
                .Where(a => a.ValueBasis == PayrollExportValueBasis.Hours)
                .Sum(a => a.ExactAmount);

            employeeTotals.Add(new PayrollExportEmployeeTotal
            {
                EmployeeId = employeeId,
                SnapshotGrossPay = snapshotGrossByEmployee[employeeId],
                ExactRowTotal = result.ExactTotal,
                RoundedRowTotal = result.RoundedTotal,
                RoundingResidual = result.RoundedTotal - result.ExactTotal,
                ProviderPricedAmount = providerPriced,
            });
        }

        var unmappedLines = unmappedByKey
            .Select(kv => new PayrollExportUnmappedLine
            {
                LineType = kv.Key.LineType,
                LineCode = kv.Key.LineCode,
                LineItemCount = kv.Value.LineItemCount,
                TotalAmount = kv.Value.TotalAmount,
                EmployeeIds = kv.Value.EmployeeIds.OrderBy(id => id).ToList(),
            })
            .OrderBy(u => u.LineType)
            .ThenBy(u => u.LineCode, StringComparer.Ordinal)
            .ToList();

        return new PayrollExportProjection
        {
            // Canonical, total order: every property test that shuffles input and expects identical
            // output depends on this, not just on-screen readability.
            Rows = allRows
                .OrderBy(r => r.EmployeeId)
                .ThenBy(r => r.WorkDate.HasValue)
                .ThenBy(r => r.WorkDate)
                .ThenBy(r => r.EarningCode, StringComparer.Ordinal)
                .ToList(),
            UnmappedLines = unmappedLines,
            EmployeesMissingExternalId = missingExternalId.OrderBy(id => id).ToList(),
            EmployeeTotals = employeeTotals,
        };
    }

    private static void AccumulateLine(
        PayLineItem line, PayCalculationSnapshot snapshot, bool hasExternalId, string? externalId,
        PayrollExportGrouping grouping, Dictionary<LineKey, PayrollEarningCodeMapping> mappingsByKey,
        Dictionary<RowGroupKey, RowAccumulator> rowGroups, Dictionary<LineKey, UnmappedAccumulator> unmappedByKey)
    {
        var lineKey = new LineKey(line.Type, line.Code);

        if (!mappingsByKey.TryGetValue(lineKey, out var mapping))
        {
            if (!unmappedByKey.TryGetValue(lineKey, out var unmapped))
            {
                unmapped = new UnmappedAccumulator();
                unmappedByKey[lineKey] = unmapped;
            }

            unmapped.LineItemCount++;
            unmapped.TotalAmount += line.Amount;
            unmapped.EmployeeIds.Add(snapshot.EmployeeId);
        }
        else if (hasExternalId)
        {
            // A mapped line for an employee with no external id contributes to neither UnmappedLines
            // nor a row — EmployeesMissingExternalId already reports the gap, and it is a different
            // problem with a different fix (link the employee) than an unmapped code (configure one).
            var workDate = grouping == PayrollExportGrouping.WorkDate ? line.ShiftDate : (LocalDate?)null;
            var groupKey = new RowGroupKey(snapshot.EmployeeId, mapping.EarningCode, workDate);

            if (!rowGroups.TryGetValue(groupKey, out var accumulator))
            {
                accumulator = new RowAccumulator
                {
                    EmployeeId = snapshot.EmployeeId,
                    ExternalEmployeeId = externalId!,
                    PeriodStart = snapshot.PeriodStart,
                    PeriodEnd = snapshot.PeriodEnd,
                    WorkDate = workDate,
                    EarningCode = mapping.EarningCode,
                    ValueBasis = mapping.ValueBasis,
                };
                rowGroups[groupKey] = accumulator;
            }

            accumulator.ExactHours += line.Hours;
            accumulator.ExactAmount += line.Amount;
            accumulator.LineItemCount++;
            accumulator.Observe(line.BaseRate);
        }
    }

    private static EmployeeRoundingResult RoundEmployeeGroup(
        List<RowAccumulator> group, PayrollExportRounding rounding)
    {
        List<PayrollExportRow> rows;
        if (group.Count == 0)
        {
            rows = [];
        }
        else
        {
            rows = rounding.Policy == PayrollExportRoundingPolicy.DistributeRemainder
                ? DistributeRemainder(group, rounding)
                : BuildWithAdjustmentRow(group, rounding);
        }

        return new EmployeeRoundingResult(rows, group.Sum(a => a.ExactAmount), rows.Sum(r => r.Amount));
    }

    private static List<PayrollExportRow> DistributeRemainder(List<RowAccumulator> group, PayrollExportRounding rounding)
    {
        var scaleFactor = Pow10(rounding.AmountScale);
        var scaledExact = group.Select(a => a.ExactAmount * scaleFactor).ToList();
        var baseline = scaledExact.Select(Math.Floor).ToList();
        var remainders = scaledExact.Select((v, i) => v - baseline[i]).ToList();

        var target = Math.Round(group.Sum(a => a.ExactAmount) * scaleFactor, 0, MidpointRounding.AwayFromZero);
        var baselineSum = baseline.Sum();

        // Always mathematically in [0, group.Count] — see the class doc comment's proof — but a
        // defensive clamp turns any violation of that proof into a bounded rounding error instead
        // of an out-of-range index.
        var extraUnits = (int)Math.Clamp(target - baselineSum, 0, group.Count);

        // Amount-basis rows first: nudging a residual cent onto an Hours-basis row's Amount is
        // inert, since a writer never reads that field for a row the provider re-prices from Hours.
        // Then by descending remainder (the rows closest to needing the extra unit get it first),
        // then by a stable index so an exact tie in remainder still resolves deterministically.
        var order = Enumerable.Range(0, group.Count)
            .OrderByDescending(i => group[i].ValueBasis == PayrollExportValueBasis.Amount)
            .ThenByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToList();

        var touched = new bool[group.Count];
        for (var k = 0; k < extraUnits; k++)
        {
            touched[order[k]] = true;
        }

        var rows = new List<PayrollExportRow>(group.Count);
        for (var i = 0; i < group.Count; i++)
        {
            var minorUnits = baseline[i] + (touched[i] ? 1 : 0);
            rows.Add(BuildRow(group[i], rounding, minorUnits / scaleFactor, touched[i]));
        }

        return rows;
    }

    /// <summary>Every row rounds independently (ordinary nearest-rounding, no distribution), so
    /// every code except the adjustment code itself stays exactly its own nearest-rounded value.
    /// Whatever gap remains between that sum and the nearest-rounded target lands on one synthetic
    /// row — simpler for an auditor to trust every other line at the cost of a configured code and
    /// an extra line on the stub. That row represents no real pay, only a rounding correction, which
    /// is why it carries LineItemCount = 0 and is excluded from ExactRowTotal's real-pay accounting.</summary>
    private static List<PayrollExportRow> BuildWithAdjustmentRow(List<RowAccumulator> group, PayrollExportRounding rounding)
    {
        var rows = new List<PayrollExportRow>(group.Count + 1);
        var roundedSum = 0m;

        foreach (var accumulator in group)
        {
            var amount = Math.Round(accumulator.ExactAmount, rounding.AmountScale, MidpointRounding.AwayFromZero);
            roundedSum += amount;
            rows.Add(BuildRow(accumulator, rounding, amount, isRoundingAdjusted: false));
        }

        var target = Math.Round(group.Sum(a => a.ExactAmount), rounding.AmountScale, MidpointRounding.AwayFromZero);
        var residual = target - roundedSum;

        if (residual != 0m)
        {
            var first = group[0];
            rows.Add(new PayrollExportRow
            {
                EmployeeId = first.EmployeeId,
                ExternalEmployeeId = first.ExternalEmployeeId,
                PeriodStart = first.PeriodStart,
                PeriodEnd = first.PeriodEnd,
                WorkDate = null,
                EarningCode = rounding.AdjustmentEarningCode,
                ValueBasis = PayrollExportValueBasis.Amount,
                Hours = 0m,
                ExactHours = 0m,
                Amount = residual,
                ExactAmount = residual,
                Rate = null,
                LineItemCount = 0,
                IsRoundingAdjusted = true,
            });
        }

        return rows;
    }

    private static PayrollExportRow BuildRow(
        RowAccumulator accumulator, PayrollExportRounding rounding, decimal amount, bool isRoundingAdjusted) => new()
    {
        EmployeeId = accumulator.EmployeeId,
        ExternalEmployeeId = accumulator.ExternalEmployeeId,
        PeriodStart = accumulator.PeriodStart,
        PeriodEnd = accumulator.PeriodEnd,
        WorkDate = accumulator.WorkDate,
        EarningCode = accumulator.EarningCode,
        ValueBasis = accumulator.ValueBasis,
        Hours = Math.Round(accumulator.ExactHours, rounding.HoursScale, MidpointRounding.AwayFromZero),
        Amount = amount,
        ExactHours = accumulator.ExactHours,
        ExactAmount = accumulator.ExactAmount,
        Rate = accumulator.ResolvedRate,
        LineItemCount = accumulator.LineItemCount,
        IsRoundingAdjusted = isRoundingAdjusted,
    };

    private static Dictionary<LineKey, PayrollEarningCodeMapping> BuildMappingIndex(
        IReadOnlyList<PayrollEarningCodeMapping> mappings)
    {
        var index = new Dictionary<LineKey, PayrollEarningCodeMapping>();
        foreach (var mapping in mappings)
        {
            // Last-write-wins on a duplicate key is unreachable in practice — the DB's partial
            // unique index on (ClientId, ProfileId, LineType, LineCode) already prevents saving two
            // active mappings for the same key — but this stays a plain assignment rather than a
            // throw so the projector never depends on that DB guarantee to avoid crashing.
            index[new LineKey(mapping.LineType, mapping.LineCode)] = mapping;
        }

        return index;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }

    private sealed record LineKey(PayLineType LineType, string LineCode);

    private sealed record RowGroupKey(int EmployeeId, string EarningCode, LocalDate? WorkDate);

    private sealed record EmployeeRoundingResult(List<PayrollExportRow> Rows, decimal ExactTotal, decimal RoundedTotal);

    private sealed class RowAccumulator
    {
        public required int EmployeeId { get; init; }
        public required string ExternalEmployeeId { get; init; }
        public required LocalDate PeriodStart { get; init; }
        public required LocalDate PeriodEnd { get; init; }
        public required LocalDate? WorkDate { get; init; }
        public required string EarningCode { get; init; }
        public required PayrollExportValueBasis ValueBasis { get; init; }

        public decimal ExactHours { get; set; }
        public decimal ExactAmount { get; set; }
        public int LineItemCount { get; set; }

        private decimal? _rate;
        private bool _rateAmbiguous;

        /// <summary>The single distinct BaseRate seen so far, or null once two different values have
        /// been observed. Once ambiguous, stays ambiguous — a third differing value changes nothing.</summary>
        public void Observe(decimal? baseRate)
        {
            if (baseRate is { } value && !_rateAmbiguous)
            {
                if (_rate is null)
                {
                    _rate = value;
                }
                else if (_rate != value)
                {
                    _rateAmbiguous = true;
                    _rate = null;
                }
            }
        }

        public decimal? ResolvedRate => _rateAmbiguous ? null : _rate;
    }

    private sealed class UnmappedAccumulator
    {
        public int LineItemCount { get; set; }
        public decimal TotalAmount { get; set; }
        public HashSet<int> EmployeeIds { get; } = [];
    }
}
