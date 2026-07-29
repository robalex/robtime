namespace TimeCalculation.Model;

/// <summary>
/// A PayResult plus the punch-level grouping graph the pipeline built on the way to it.
///
/// Exists because PayResult is pay itemization only: its deepest punch reference is
/// ShiftPay/PayLineItem.AnchorPunchId, a single id per shift. Nothing in it carries in/out times,
/// raw-vs-rounded, inferred Break/Lunch subtype, per-pair position/rate, or the incomplete
/// (orphan In-only / Out-only) pairs — all of which a timecard has to render (UI_PLAN.md §8,
/// "week → day → shift → punch pair").
///
/// That detail cannot be recovered by joining Punch rows back on AnchorPunchId afterwards: which
/// punches belong to which shift is a *pipeline output* (ShiftBuilder splits on
/// DistanceBetweenShiftsHours), and only the anchor survives into PayResult. Reconstructing the rest
/// would mean re-running that grouping — which is exactly the engine-version-dependent computation
/// UI_PLAN.md decision 24 freezes a snapshot to avoid. So the graph is returned from the one run
/// that already built it, rather than rebuilt by a caller.
///
/// Weeks is the *grouping* graph (Workweek → WorkDay → Shift → PunchPair → Punch); Result.Workweeks
/// is the *pay* graph (WorkweekPay → ShiftPay → PayLineItem). They describe the same period from two
/// angles and are joined on (ShiftDate, AnchorPunchId) — the identity scheme Shift.AnchorPunchId
/// already exists to provide. Note the shifts reachable through Weeks are pre-premium: premiums are
/// applied per-week during summarization and land in Result as PayLineItems, so read pay from Result
/// and structure from Weeks, never the reverse.
/// </summary>
public record PayCalculationDetail
{
    public PayResult Result { get; init; } = new();

    public IReadOnlyList<Workweek> Weeks { get; init; } = [];
}
