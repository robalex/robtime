using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>Rounding/scale settings a <see cref="PayrollExportProjector"/> run needs — mirrors the
/// relevant subset of <see cref="PayrollExportProfile"/>, kept separate so the projector never
/// depends on the EF entity type for anything but the mapping rows themselves.</summary>
public sealed record PayrollExportRounding
{
    public required int AmountScale { get; init; }
    public required int HoursScale { get; init; }
    public required PayrollExportRoundingPolicy Policy { get; init; }

    /// <summary>Required only when Policy is AdjustmentRow.</summary>
    public required string AdjustmentEarningCode { get; init; }
}

public sealed record PayrollExportProjectionInput
{
    public required IReadOnlyList<PayCalculationSnapshot> Snapshots { get; init; }
    public required IReadOnlyList<PayrollEarningCodeMapping> Mappings { get; init; }
    public required IReadOnlyDictionary<int, string> ExternalIdsByEmployeeId { get; init; }
    public required PayrollExportGrouping Grouping { get; init; }
    public required PayrollExportRounding Rounding { get; init; }
}

public sealed record PayrollExportRow
{
    public required int EmployeeId { get; init; }
    public required string ExternalEmployeeId { get; init; }
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }

    /// <summary>Null when Grouping is PayPeriod; the shared PayLineItem.ShiftDate of every
    /// contributing line when Grouping is WorkDate.</summary>
    public LocalDate? WorkDate { get; init; }

    public required string EarningCode { get; init; }
    public required PayrollExportValueBasis ValueBasis { get; init; }

    /// <summary>Rounded to Rounding.HoursScale — what a file would emit.</summary>
    public required decimal Hours { get; init; }

    /// <summary>Rounded to Rounding.AmountScale, after residual settlement — what a file would emit.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Full precision, pre-rounding. The reconciliation basis; never written to a file.</summary>
    public required decimal ExactHours { get; init; }
    public required decimal ExactAmount { get; init; }

    /// <summary>The single distinct non-null BaseRate across the contributing lines, or null when
    /// they differed (a multi-rate week collapsed into one row) or none carried one. When set on a
    /// Hours-basis row, a writer can emit a rate-override column instead of trusting the provider's
    /// stored rate; when null, that gap is real and worth surfacing.</summary>
    public decimal? Rate { get; init; }

    /// <summary>How many real PayLineItems fed this row. Zero only for the synthetic row an
    /// AdjustmentRow policy emits to carry a rounding residual — that row represents no actual pay,
    /// only a rounding correction, and is excluded from conservation bookkeeping for exactly that
    /// reason (see PayrollExportEmployeeTotal.ExactRowTotal).</summary>
    public required int LineItemCount { get; init; }

    public required bool IsRoundingAdjusted { get; init; }
}

public sealed record PayrollExportUnmappedLine
{
    public required PayLineType LineType { get; init; }
    public required string LineCode { get; init; }
    public required int LineItemCount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required IReadOnlyList<int> EmployeeIds { get; init; }
}

public sealed record PayrollExportEmployeeTotal
{
    public required int EmployeeId { get; init; }

    /// <summary>The snapshot's own PayResult.GrossPay — every dollar this employee is actually owed
    /// for the period, mapped or not.</summary>
    public required decimal SnapshotGrossPay { get; init; }

    /// <summary>Sum of ExactAmount over this employee's REAL rows (LineItemCount > 0) — the
    /// full-precision total actually captured in Rows. Zero for an employee with no resolvable
    /// external id, since no rows can be built for them at all; SnapshotGrossPay minus this is
    /// exactly the money not yet represented anywhere in Rows.</summary>
    public required decimal ExactRowTotal { get; init; }

    /// <summary>Sum of Amount over every emitted row, including a synthetic AdjustmentRow-policy row
    /// if one exists — the total that would actually appear in an export file for this employee.</summary>
    public required decimal RoundedRowTotal { get; init; }

    public required decimal RoundingResidual { get; init; }

    /// <summary>The portion of ExactRowTotal sitting on Hours-basis rows — money this export does
    /// NOT assert a dollar figure for, because the provider re-prices it from its own stored rate.
    /// Naming it is what keeps the reconciliation claim honest rather than overstated.</summary>
    public required decimal ProviderPricedAmount { get; init; }
}

public sealed record PayrollExportProjection
{
    public required IReadOnlyList<PayrollExportRow> Rows { get; init; }
    public required IReadOnlyList<PayrollExportUnmappedLine> UnmappedLines { get; init; }
    public required IReadOnlyList<int> EmployeesMissingExternalId { get; init; }
    public required IReadOnlyList<PayrollExportEmployeeTotal> EmployeeTotals { get; init; }

    public bool IsComplete => UnmappedLines.Count == 0 && EmployeesMissingExternalId.Count == 0;
}
