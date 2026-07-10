namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// Overtime pay for a workweek, exposed in both representations described in the plan so the
/// consumer picks one at serialization time.  Both derive from the same allocation and rate, so
/// <see cref="TotalPay"/> equals <see cref="StraightTime"/> + <see cref="OvertimePremium"/>.
/// </summary>
public record OvertimeResult
{
    public OvertimeAllocation Allocation { get; init; } = new();
    public decimal RegularRate { get; init; }

    // ── Composed view: regular + overtime + doubletime pay ──
    public decimal RegularPay => Allocation.RegularHours * RegularRate;
    public decimal OvertimePay => Allocation.OvertimeHours * 1.5m * RegularRate;
    public decimal DoubletimePay => Allocation.DoubletimeHours * 2.0m * RegularRate;
    public decimal TotalPay => RegularPay + OvertimePay + DoubletimePay;

    // ── Premium view: all hours at straight time, plus the overtime premium on top ──
    public decimal StraightTime => Allocation.TotalHours * RegularRate;
    public decimal OvertimePremium =>
        Allocation.OvertimeHours * 0.5m * RegularRate + Allocation.DoubletimeHours * 1.0m * RegularRate;
}
