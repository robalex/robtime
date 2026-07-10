namespace TimeCalculation.Model;

public enum PayPeriodFrequency
{
    Weekly,        // 7-day period anchored to a reference date
    BiWeekly,      // 14-day period anchored to a reference date
    SemiMonthly,   // 1st–15th and 16th–end-of-month
    Monthly,       // calendar month
}
