namespace TimeCalculation.Model;

/// <summary>
/// Classifies the mid-shift gap between an Out punch and the next In punch.
/// Carried by both punches bounding the gap.  Null on a Punch means "not yet
/// resolved — infer in Stage 2"; a non-null value set before Stage 2 is a
/// forced designation and is never overwritten by inference.
/// </summary>
public enum PunchSubtype
{
    None,   // Not a break/lunch boundary (e.g. first In / last Out of a shift)
    Break,
    Lunch,
}
