namespace TimeCalculation.Model;

/// <summary>
/// Distinguishes bonus treatment for regular-rate-of-pay (FLSA 29 CFR §778).
/// Non-discretionary bonuses must be folded into the regular rate; discretionary ones are excluded.
/// </summary>
public enum BonusKind
{
    Discretionary,      // excluded from RROP
    NonDiscretionary,   // included in RROP (spread across hours worked)
}
