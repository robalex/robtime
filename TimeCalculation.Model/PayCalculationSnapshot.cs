using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A frozen calculation — everything a timecard needs to render, pinned at the moment a supervisor
/// approves it, so a later engine change can never alter pay that was already signed off (UI_PLAN.md
/// decision 24). Persisted as one JSON blob on TimecardApproval (Phase 6.7) — "calculation output,
/// not a relational aggregate," same reasoning PayrollDbContext's own class doc already gives.
///
/// Carries the full grouping graph (Weeks), not just the pay itemization (Result) — decision 25:
/// a PayResult alone has no punch times, only an AnchorPunchId per shift, and re-deriving the rest by
/// joining live Punch rows would make a "frozen" render depend on the engine's current grouping logic
/// again, defeating the point. See PayCalculationDetail, whose shape this mirrors — Weeks is the
/// *grouping* graph, Result is the *pay* graph, joined on (ShiftDate, AnchorPunchId).
///
/// EmployeeId only, never a name — snapshots are retained indefinitely for audit, so this is a
/// PII-retention surface that should stay minimal; resolve a name at read time.
/// </summary>
public record PayCalculationSnapshot
{
    public int EmployeeId { get; init; }
    public int ClientId { get; init; }
    public LocalDate PeriodStart { get; init; }
    public LocalDate PeriodEnd { get; init; }
    public Instant CalculatedAt { get; init; }

    /// <summary>Identifies which build of the engine produced this — the actual answer to "did a bug
    /// fix change this number," which the rule-version fields below can't answer on their own (they
    /// name which config was used, not which code interpreted it). ModuleVersionId is a GUID unique
    /// per compiled build with zero extra tooling required, not a semantic version — good enough to
    /// tell two snapshots apart, not to describe what changed between them.</summary>
    public string EngineVersion { get; init; } = string.Empty;

    public int PayRuleId { get; init; }
    public string PayRuleName { get; init; } = string.Empty;
    public int PayRuleVersion { get; init; }

    public PayResult Result { get; init; } = new();
    public IReadOnlyList<Workweek> Weeks { get; init; } = [];
}
