using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Persistence;

/// <summary>
/// How this client wants a <see cref="PayLineType"/>/code pair valued when it leaves RobTime for a
/// payroll provider: as the raw hours (the provider re-prices them from its own stored rate) or as
/// the computed dollar amount (RobTime's own price stands).
/// </summary>
public enum PayrollExportValueBasis
{
    Hours,
    Amount,
}

/// <summary>Which payroll system a <see cref="PayrollExportProfile"/> targets. GenericCsv needs no
/// partner agreement or API credentials and is the only one with a writer in the early slices; the
/// named providers exist so real integrations (and their own employee-id namespace — see
/// <see cref="PayrollEmployeeIdentifier"/>) have somewhere to attach later without a schema change.</summary>
public enum PayrollProvider
{
    GenericCsv,
    Adp,
    Paychex,
    Gusto,
}

/// <summary>Row-granularity for a payroll export. See <see cref="PayrollExportProfile.Grouping"/>.</summary>
public enum PayrollExportGrouping
{
    /// <summary>One row per (employee, earning code) for the whole period — how batch paydata import
    /// actually works in most providers, and the smallest file.</summary>
    PayPeriod,

    /// <summary>One row per (employee, earning code, work date) — needed for job costing, certified
    /// payroll, or multi-jurisdiction local taxes where the work date determines the taxing locality.</summary>
    WorkDate,
}

/// <summary>How a projector settles the pennies that full-precision aggregation and 2-decimal rounding
/// don't agree on. See <see cref="PayrollExportProfile.AdjustmentEarningCode"/>.</summary>
public enum PayrollExportRoundingPolicy
{
    /// <summary>Nudge ±1 cent onto individual rows (preferring Amount-basis ones) until the rounded
    /// total matches the exact total. A penny can move between earning codes; every row stays
    /// individually explainable on a pay stub.</summary>
    DistributeRemainder,

    /// <summary>Leave every row's own rounding alone and dump the residual on a single dedicated
    /// earning code (<see cref="PayrollExportProfile.AdjustmentEarningCode"/>). Keeps every other
    /// code exact, at the cost of a configured code and a memo line on the stub.</summary>
    AdjustmentRow,
}

/// <summary>
/// A client's payroll export configuration: which provider, how rows are grouped and rounded, and
/// the decimal precision to emit at. The parent <see cref="PayrollEarningCodeMapping"/> and
/// <see cref="PayrollEmployeeIdentifier"/> rows hang off this rather than off the client directly,
/// because a client running two providers in parallel during a cutover needs two independent
/// mapping sets and two independent employee-id namespaces — an ADP Associate ID is not a Gusto
/// employee id, and conflating them onto one client-wide config would make that dual-run
/// unrepresentable. A single-provider client just has one profile.
///
/// Not effective-dated, unlike <see cref="PayRules.PayRule"/> or <see cref="HolidayCalendar"/>: this
/// is integration configuration, read as "how does this client's export work right now," not a
/// payroll-consequential setting with its own history to preserve.
/// </summary>
public class PayrollExportProfile
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PayrollProvider Provider { get; set; }
    public PayrollExportGrouping Grouping { get; set; } = PayrollExportGrouping.PayPeriod;
    public PayrollExportRoundingPolicy RoundingPolicy { get; set; } = PayrollExportRoundingPolicy.DistributeRemainder;

    /// <summary>Required only when RoundingPolicy is AdjustmentRow — the provider earning code the
    /// rounding residual is dumped on.</summary>
    public string AdjustmentEarningCode { get; set; } = string.Empty;

    public int AmountScale { get; set; } = 2;
    public int HoursScale { get; set; } = 2;
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Maps one (<see cref="PayLineType"/>, LineCode) pair — the same key a <see cref="PayLineItem"/>
/// carries — to this client's provider earning code and tells the exporter whether to send hours or
/// a dollar amount for it. LineCode mirrors PayLineItem.Code exactly: empty string (never null) for
/// Regular/FixedHours, "OVERTIME"/"DOUBLETIME" for OvertimePremium, a BonusKind name for Bonus, and
/// the differential/premium rule's own code for Differential/Premium.
///
/// Unique per (ClientId, ProfileId, LineType, LineCode) among non-deleted rows: one earning code per
/// line kind. A client wanting, say, OT dollars under one code and OT hours as a memo under a second
/// code can't express that in v1 — relaxing it later means dropping the uniqueness constraint and
/// adding an ordinal, which is additive, not a breaking change.
/// </summary>
public class PayrollEarningCodeMapping
{
    public int Id { get; set; }

    // Denormalized from the profile's own ClientId, matching the tenancy convention used everywhere
    // else in this schema (Phase 0) — retrofitting a missing ClientId column is the expensive
    // direction, so every tenant-scoped table carries its own rather than reaching it via a join.
    public int ClientId { get; set; }
    public int ProfileId { get; set; }

    public PayLineType LineType { get; set; }
    public string LineCode { get; set; } = string.Empty;
    public string EarningCode { get; set; } = string.Empty;
    public PayrollExportValueBasis ValueBasis { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

/// <summary>
/// This employee's identifier in one payroll provider's own system — an ADP Associate ID, a Gusto
/// employee id. Deliberately separate from any inbound timeclock credential the Employee record
/// might carry (e.g. a future badge number): that resolves badge-swipe to EmployeeId, client-scoped,
/// inbound; this resolves EmployeeId to the provider's own id, profile-scoped, outbound. Different
/// direction, different uniqueness domain, different lifecycle — they must not share a column.
///
/// Two independent uniqueness constraints among non-deleted rows: at most one identifier per
/// (ProfileId, EmployeeId), and — the one that actually protects someone's paycheck — at most one
/// employee per (ProfileId, ExternalEmployeeId). Without the second, two RobTime employees could map
/// to the same payroll id and silently merge two people's pay into one.
/// </summary>
public class PayrollEmployeeIdentifier
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int ProfileId { get; set; }
    public int EmployeeId { get; set; }
    public string ExternalEmployeeId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

/// <summary>
/// One payroll export run — the historical record of what was actually generated and (once
/// downloaded) handed to a provider. The generated file is stored verbatim on <see cref="FileContent"/>
/// rather than regenerated on request: like <c>TimecardApproval.SnapshotJson</c> freezing pay so a
/// later engine change can never retroactively alter what was already paid, storing the bytes here
/// freezes the export so a later mapping/identifier edit can't silently change what "download this
/// batch again" returns. Typical file sizes are KB, not MB — a bytea column costs nothing worth
/// avoiding for that guarantee.
///
/// Follows <see cref="PunchImportBatch"/>'s shape, not the standard IsDeleted pattern the other three
/// Payroll* entities use: a batch is never truly removed, only voided. <see cref="VoidedAt"/> being
/// null is "currently valid" — same UnapprovedAt-style naming <see cref="TimecardApproval"/> already
/// uses for "this row is superseded but stays in history." Re-exporting the same
/// (ProfileId, PeriodStart, PeriodEnd) is hard-blocked while a non-voided batch exists for it (a
/// partial unique index, not just a service-level check) — double-paying a period is not a mistake
/// this schema lets you make by accident.
/// </summary>
public class PayrollExportBatch
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int ProfileId { get; set; }
    public LocalDate PeriodStart { get; set; }
    public LocalDate PeriodEnd { get; set; }
    public int EmployeeCount { get; set; }
    public int RowCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] FileContent { get; set; } = [];
    public string ExportedByUserId { get; set; } = string.Empty;
    public Instant ExportedAt { get; set; }

    public string? VoidedByUserId { get; set; }
    public Instant? VoidedAt { get; set; }
}
