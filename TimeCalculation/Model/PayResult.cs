using NodaTime;

namespace TimeCalculation.Model;

public enum PayLineType
{
    Regular,           // straight-time earnings (hours × rate)
    OvertimePremium,   // the half/full-time uplift on OT/DT hours
    Differential,      // time-based differential
    Bonus,             // FixedDollar bonus (discretionary or not — both are paid)
    FixedHours,        // flat hours added (e.g. paid leave)
    Premium,           // meal/rest statutory penalty
}

/// <summary>
/// One itemized line of a pay result, retained so a pay statement can be reconstructed. ShiftDate
/// and AnchorPunchId identify which shift the line belongs to (same identity scheme as
/// PremiumResult — see Shift.AnchorPunchId), so a UI can answer "why was this shift paid this way."
/// </summary>
public record PayLineItem
{
    public PayLineType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Hours { get; init; }
    public decimal Amount { get; init; }
    public LocalDate ShiftDate { get; init; }
    public int AnchorPunchId { get; init; }
}

/// <summary>
/// Pay for a single shift, broken out of its workweek so a UI can show why that piece of a
/// workday was paid the way it was — which rate(s) applied, which differentials/premiums fired,
/// and how much (if any) of its hours landed in overtime.
/// </summary>
public record ShiftPay
{
    public LocalDate ShiftDate { get; init; }
    public int AnchorPunchId { get; init; }
    public IReadOnlyList<PayLineItem> LineItems { get; init; } = [];

    public decimal Gross => LineItems.Sum(l => l.Amount);
}

/// <summary>
/// Pay for a single workweek: the per-shift breakdown plus week-level totals. LineItems is the
/// flattened view across all shifts (kept for callers that just want the week's line items
/// without drilling into which shift each one came from); it is always exactly the concatenation
/// of Shifts[*].LineItems, so the two can never drift.
/// </summary>
public record WorkweekPay
{
    public LocalDate WeekStart { get; init; }
    public decimal RegularRate { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal DoubletimeHours { get; init; }
    public IReadOnlyList<ShiftPay> Shifts { get; init; } = [];

    public IReadOnlyList<PayLineItem> LineItems => Shifts.SelectMany(s => s.LineItems).ToList();
    public decimal Gross => Shifts.Sum(s => s.Gross);
}

/// <summary>
/// Stage 13 output: the itemized pay for an employee across the workweeks derived from a set of
/// punches.  Uses the FLSA "premium" representation — straight-time earnings paid at actual rates,
/// plus the overtime premium on top — so differentials and bonuses are never double-counted.
/// </summary>
public record PayResult
{
    public int EmployeeId { get; init; }
    public IReadOnlyList<WorkweekPay> Workweeks { get; init; } = [];

    public IReadOnlyList<PayLineItem> LineItems => Workweeks.SelectMany(w => w.LineItems).ToList();
    public decimal GrossPay => Workweeks.Sum(w => w.Gross);
}
