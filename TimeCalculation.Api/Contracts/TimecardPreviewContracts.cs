using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// One row of the Phase 6.8 bulk-entry grid, not yet saved — deliberately lighter than
/// <see cref="CreatePunchRequest"/> (no EmployeeId, echoed by the route; no device idempotency
/// fields, meaningless for a hand-typed draft row) rather than reusing that type, since a preview
/// request is a genuinely different thing from a create request: nothing here is validated as
/// strictly as <see cref="Validation.PunchRequestValidator"/> requires for an actual save, because an
/// incomplete row mid-entry (e.g. FixedDollar with no Amount yet) should preview as best it can, not
/// fail the whole request — the real validation still runs at save time, via
/// <see cref="PunchService.CreateBatchAsync"/>.
/// </summary>
public sealed record DraftPunchEntry
{
    public required Instant PunchTime { get; init; }
    public required PunchKind Kind { get; init; }
    public PunchSubtype? Subtype { get; init; }
    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Hours { get; init; }
    public BonusKind? BonusKind { get; init; }
    public bool CountsTowardRegularRate { get; init; }
}

public sealed record PreviewPunchesRequest
{
    public required List<DraftPunchEntry> DraftPunches { get; init; }
}

/// <summary>
/// A compact running total for the bulk-entry grid — "the week's total updates as punches are typed"
/// (UI_PLAN.md's Phase 6.8 design note) needs per-week hours/gross, not the full
/// week→day→shift→pair breakdown <c>TimecardResponse</c> carries for the read screen. Computed from
/// the period's real, already-saved punches plus the grid's current draft rows merged in — so what a
/// supervisor sees while typing is the period's actual resulting total, not the draft rows in
/// isolation.
/// </summary>
public sealed record BulkPunchPreviewResponse
{
    public required decimal GrossPay { get; init; }
    public required List<PreviewWeekSummary> Weeks { get; init; }
}

public sealed record PreviewWeekSummary
{
    public required LocalDate WeekStart { get; init; }
    public required decimal RegularHours { get; init; }
    public required decimal OvertimeHours { get; init; }
    public required decimal DoubletimeHours { get; init; }
    public required decimal Gross { get; init; }

    public static PreviewWeekSummary FromDomain(WorkweekPay week) => new()
    {
        WeekStart = week.WeekStart,
        RegularHours = week.RegularHours,
        OvertimeHours = week.OvertimeHours,
        DoubletimeHours = week.DoubletimeHours,
        Gross = week.Gross,
    };
}
