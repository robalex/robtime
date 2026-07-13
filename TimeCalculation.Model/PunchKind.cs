namespace TimeCalculation.Model;

public enum PunchKind
{
    In,           // Clock-in (employee-selected or supervisor-corrected)
    Out,          // Clock-out (employee-selected or supervisor-corrected)
    FixedDollar,  // Flat dollar amount added to pay; skips pairing
    FixedHours,   // Flat hours at a given rate; skips pairing
}
