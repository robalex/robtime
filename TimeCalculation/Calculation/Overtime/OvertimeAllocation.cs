namespace TimeCalculation.Calculation.Overtime;

/// <summary>
/// A workweek's hours split into pay tiers by an IOvertimeRule.  The three buckets are mutually
/// exclusive and sum to the hours worked in the week.
/// </summary>
public record OvertimeAllocation
{
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }     // paid at 1.5×
    public decimal DoubletimeHours { get; init; }   // paid at 2.0×

    public decimal TotalHours => RegularHours + OvertimeHours + DoubletimeHours;
}
