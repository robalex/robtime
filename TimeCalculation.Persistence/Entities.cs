using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Persistence;

/// <summary>
/// Persistence-shape entities for the effective-dated assignment tables.  The pure-domain records
/// (PayRuleAssignment, EmployeePositionAssignment) are immutable value types the calculator consumes;
/// these mutable POCOs are what EF Core maps, keeping the domain free of persistence concerns.
/// </summary>
public class PayRuleAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PayRuleId { get; set; }
    public PayRule PayRule { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }

    public PayRuleAssignment ToDomain() => new(PayRule, EffectiveFrom, EffectiveTo);
}

public class EmployeePositionAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }
    public decimal? Rate { get; set; }

    public EmployeePositionAssignment ToDomain() => new(Position, EffectiveFrom, EffectiveTo, Rate);
}

/// <summary>
/// Thin profile/authorization row — NOT a credential store. Amazon Cognito owns passwords, MFA, and
/// password reset (UI_PLAN.md §5); this table exists only because the API still needs ClientId/
/// EmployeeId/Role for FK targets and tenant-filtered listing. Keyed by the Cognito `sub` claim
/// (stable across the user's lifetime, survives SSO federation later) rather than a synthetic int —
/// there's no join-performance reason to prefer an int surrogate at this table's size, and using
/// `sub` directly means resolving "who is this request" never needs a lookup: it's already the JWT.
/// </summary>
public class AppUser
{
    public string CognitoSub { get; set; } = string.Empty;

    /// <summary>Null only for SystemAdmin — see AppRole's doc comment.</summary>
    public int? ClientId { get; set; }

    /// <summary>Set when this user IS an employee (self-service access); null for admin/supervisor
    /// accounts with no corresponding Employee row.</summary>
    public int? EmployeeId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public AppRole Role { get; set; }
}

/// <summary>
/// Client-wide *operational* settings — flags that are neither effective-dated nor premium-scoped,
/// which is what makes them a poor fit for <c>ClientPremiumPolicy</c> (UI_PLAN.md §9 decision 20).
/// One row per client, keyed by <c>ClientId</c> directly rather than a surrogate int — there's only
/// ever one, so a separate unique index would just duplicate the primary key. Deliberately not
/// effective-dated: contrast with <c>PayRule.PayPeriodFrequency</c> (decision 18), which needed
/// dating because a pay-period change has real payroll consequences. These flags don't carry that
/// kind of history — they're read as "what's true right now," never resolved as-of a date.
/// </summary>
public class ClientSettings
{
    public int ClientId { get; set; }

    /// <summary>Default true. When true, editing/deleting a punch creates a <c>PunchChangeRequest</c>
    /// instead of applying directly — approval is what actually mutates the <c>Punch</c> and writes
    /// the <c>PunchAuditEntry</c> (Phase 6, UI_PLAN.md §9 decision 17). When false, an authorized edit
    /// applies directly, still writing the audit entry.</summary>
    public bool RequirePunchEditApproval { get; set; } = true;

    /// <summary>Default false. When false, an Employee's own timecard shows hours/rate/gross/premiums
    /// but not the weighted FLSA regular-rate derivation; Supervisor+ always sees the full
    /// <c>PayLineItem</c> breakdown regardless of this flag (Phase 6, UI_PLAN.md §9 decision 19).
    /// </summary>
    public bool ShowFullPayItemizationToEmployees { get; set; }
}

/// <summary>
/// A proposed change to a Punch (add/edit/delete), pending Supervisor+ review — UI_PLAN.md's Phase 6
/// "Punch edit approval" design. Retained regardless of outcome: it is its own record of *who asked
/// for what and why*, deliberately distinct from <c>PunchAuditEntry</c>'s record of *what was
/// actually applied* — a denied request produces one of these but never a PunchAuditEntry, and a
/// direct edit (ClientSettings.RequirePunchEditApproval off, or a Supervisor+ editing directly via
/// PUT /punches/{id}, which "already holds approval authority") produces the audit entry with no
/// request at all. RequirePunchEditApproval itself isn't consulted anywhere in this entity or the
/// service that manages it — it becomes load-bearing once Phase 6.4's self-service employee flow
/// decides whether an edit attempt must go through this table or can apply directly.
///
/// Explicit typed columns for the requested field values (Requested*), not a JSON blob — unlike
/// PunchAuditEntry's snapshots (which exist to preserve a variable historical shape forever), this
/// table is actively queried and rendered by a live UI (the pending-request queue, Phase 6.6), so
/// sortable/filterable columns matter more here than they do for an audit trail.
/// </summary>
public class PunchChangeRequest
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>Null only while ChangeKind is Add and the request is still Pending — approval
    /// backfills this with the newly-created Punch's Id, so an approved Add request stays
    /// traceable to the punch it produced, same as Edit/Delete already are.</summary>
    public int? PunchId { get; set; }

    public PunchChangeKind ChangeKind { get; set; }

    // Requested field values. Meaningful for Add (PunchTime/Kind required — enforced at submission,
    // not deferred to approval, since there's no existing punch to merge against and fall back on)
    // and Edit (same partial-patch semantics as UpdatePunchRequest: null = leave the existing punch's
    // value alone). Ignored entirely for Delete.
    public Instant? RequestedPunchTime { get; set; }
    public string? RequestedPunchTimeZoneId { get; set; }
    public PunchKind? RequestedKind { get; set; }
    public PunchSubtype? RequestedSubtype { get; set; }
    public int? RequestedPositionId { get; set; }
    public decimal? RequestedAmount { get; set; }
    public decimal? RequestedHours { get; set; }
    public BonusKind? RequestedBonusKind { get; set; }
    public bool? RequestedCountsTowardRegularRate { get; set; }

    /// <summary>Cognito sub of whoever submitted this — becomes PunchAuditEntry.ActorUserId on
    /// approval (the audit trail's "who made this change" is whoever authored the proposed values,
    /// not whoever clicked approve; the reviewer's identity has its own dedicated fields below).
    /// </summary>
    public string RequesterUserId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }

    public PunchChangeRequestStatus Status { get; set; } = PunchChangeRequestStatus.Pending;
    public string? ReviewerUserId { get; set; }
    public Instant? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>
/// One employee's pay period, approved for payroll — UI_PLAN.md's Phase 6.7, decision 21. Locks the
/// period (every punch-mutating path checks for an active row here — TimecardLockService) and freezes
/// its pay (the <c>Snapshot</c> a reviewer's timecard renders from once locked, so a later engine or
/// config change can never alter pay that was already signed off — decision 24).
///
/// Append-only, not a mutable current-state row (decided 2026-07-28, UI_PLAN.md decision 21):
/// approving again after an un-approve writes a *new* row rather than reusing this one. "Currently
/// locked" is "the latest row for (EmployeeId, PeriodStart, PeriodEnd) has UnapprovedAt == null," not
/// a status flag — an un-approve → edit → re-approve cycle then keeps both calculations, which is
/// exactly the record payroll needs ("we paid X on the 14th, corrected to Y on the 20th"), rather than
/// overwriting the history that matters. UnapprovedAt/UnapprovedByUserId are the one field this row's
/// own lifecycle still mutates once, to close it out — that's a smaller claim than "this row is a
/// current-state row," since a fresh approve after that never touches it again.
///
/// A `Pending` PunchChangeRequest blocks creating one of these (decision 23) — by construction, then,
/// no approved row's period can have a pending request against it, so nothing here re-checks that
/// invariant once a row exists.
/// </summary>
public class TimecardApproval
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public LocalDate PeriodStart { get; set; }
    public LocalDate PeriodEnd { get; set; }

    public string ApprovedByUserId { get; set; } = string.Empty;
    public Instant ApprovedAt { get; set; }

    public string? UnapprovedByUserId { get; set; }
    public Instant? UnapprovedAt { get; set; }

    /// <summary>The frozen PayCalculationSnapshot, JSON-serialized — one column, not a normalized
    /// graph, same "calculation output, not a relational aggregate" reasoning PayCalculationSnapshot's
    /// own doc comment gives. See TimecardSnapshotSerializer for the serialization convention (mirrors
    /// PunchAuditor's punch-snapshot approach: NodaTime-aware System.Text.Json, never touches an HTTP
    /// response pipeline directly).</summary>
    public string SnapshotJson { get; set; } = string.Empty;
}

/// <summary>
/// One bulk CSV punch import — the tag every <c>Punch.ImportBatchId</c> it produced points back to,
/// and what "undo this import" (PunchImportService.DeleteBatchAsync) operates on. <c>Id</c> *is* the
/// batch code; there's no separate human-facing code column, since the numeric id is already what a
/// "delete batch 42" action needs and nothing else references a batch by any other name.
///
/// Deleting a batch hard-deletes its Punch rows (not the usual IsDeleted soft-delete every other
/// tenant-scoped entity gets) — decided explicitly: an import can be large enough that leaving a bad
/// one soft-deleted-but-present forever is real, pointless bloat, unlike a single mis-entered punch.
/// This row itself is never deleted, only marked via Deleted*— one row regardless of how large the
/// batch was, so keeping it costs nothing and preserves "who imported what, and who later removed
/// it" as real history. The PunchAuditEntry "Created" rows the import wrote are kept too, for the
/// same reason every other punch-mutating path never touches audit history when the punch it
/// describes later goes away (PunchAuditEntry.PunchId is deliberately not a real FK, precisely so it
/// can outlive the punch) — they're inert once the punch is gone, never read by pay calculation,
/// and record something that's still true after the delete ("this punch existed, briefly").
/// </summary>
public class PunchImportBatch
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int PunchCount { get; set; }
    public string ImportedByUserId { get; set; } = string.Empty;
    public Instant ImportedAt { get; set; }

    public string? DeletedByUserId { get; set; }
    public Instant? DeletedAt { get; set; }
}
