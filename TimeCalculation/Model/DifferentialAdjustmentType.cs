namespace TimeCalculation.Model;

public enum DifferentialAdjustmentType
{
    FlatPerHour,   // add AdjustmentValue dollars per qualifying hour
    Multiplier,    // add baseRate * AdjustmentValue per qualifying hour (AdjustmentValue is the extra fraction)
    FixedBonus,    // add AdjustmentValue once when the shift qualifies
}
