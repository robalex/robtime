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

/// <summary>One itemized line of a pay result, retained so a pay statement can be reconstructed.</summary>
public record PayLineItem
{
    public PayLineType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Hours { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>Pay for a single workweek: the itemized lines plus the gross.</summary>
public record WorkweekPay
{
    public LocalDate WeekStart { get; init; }
    public decimal RegularRate { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal DoubletimeHours { get; init; }
    public IReadOnlyList<PayLineItem> LineItems { get; init; } = [];

    public decimal Gross => LineItems.Sum(l => l.Amount);
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
