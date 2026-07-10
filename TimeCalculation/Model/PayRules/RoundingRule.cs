namespace TimeCalculation.Model.PayRules;

public class RoundingRule
{
    public RoundingStrategy RoundingStrategy { get; set; } = RoundingStrategy.None;
    public int RoundingIntervalMinutes { get; set; } = 15;
    public int RoundingGraceMinutes { get; set; } = 7;
}
