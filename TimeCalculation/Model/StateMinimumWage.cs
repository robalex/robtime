using NodaTime;

namespace TimeCalculation.Model;

/// <summary>Effective-dated per-state minimum wage lookup (reference data).</summary>
public class StateMinimumWage
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }
    public decimal Amount { get; set; }
}
