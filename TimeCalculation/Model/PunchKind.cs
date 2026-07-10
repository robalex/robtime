namespace TimeCalculation.Model;

public enum PunchKind
{
    Clock,        // Raw event; In/Out determined by Stage 2 inference
    In,           // Clock-in (inferred or supervisor-corrected)
    Out,          // Clock-out (inferred or supervisor-corrected)
    FixedDollar,  // Flat dollar amount added to pay; skips pairing
    FixedHours,   // Flat hours at a given rate; skips pairing
}
