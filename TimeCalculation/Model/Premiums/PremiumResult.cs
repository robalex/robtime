using NodaTime;

namespace TimeCalculation.Model.Premiums;

/// <summary>
/// The outcome of a premium rule against one shift, with an audit explanation of why the amount
/// was (or was not) paid.  A rule that does not apply, or whose violation was waived, returns
/// Amount = 0 with the reason recorded.
/// </summary>
public record PremiumResult
{
    public string Code { get; init; } = string.Empty;
    public decimal Hours { get; init; }        // premium hours (e.g. 1.0, or 0.5 for WA)
    public decimal Amount { get; init; }       // dollars paid

    /// <summary>The workweek's regular rate of pay — what this premium is priced from. Always
    /// populated, even when compliant/waived, so a UI can show what the charge WOULD be.</summary>
    public decimal BaseRate { get; init; }

    /// <summary>The multiple of BaseRate actually charged per hour: 1.0 for most premiums (paid at
    /// the regular rate), 1.5 for Puerto Rico's meal premium (paid at the overtime rate). Amount is
    /// Hours × BaseRate × Multiplier whenever the premium is paid.</summary>
    public decimal Multiplier { get; init; }

    public bool Violated { get; init; }        // a violation was detected
    public bool Waived { get; init; }          // detected but waived by override
    public string Explanation { get; init; } = string.Empty;

    /// <summary>How this premium may be waived — lets a UI decide whether to offer a waive control,
    /// and whether it needs supervisor, employee, or both.</summary>
    public WaiverPolicy WaiverPolicy { get; init; }

    // Stable per-shift identity: (AnchorPunchId, Code) survives recomputation so a recorded override
    // can be matched back to the same premium. Do NOT key on (ShiftDate, Code) — that is the per-day
    // cap key and is ambiguous when a workday has multiple shifts. ShiftDate is here for display/grouping.
    public int AnchorPunchId { get; init; }
    public LocalDate ShiftDate { get; init; }

    public bool IsPaid => Amount > 0;
}
