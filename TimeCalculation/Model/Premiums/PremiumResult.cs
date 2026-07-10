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
    public bool Violated { get; init; }        // a violation was detected
    public bool Waived { get; init; }          // detected but waived by override
    public string Explanation { get; init; } = string.Empty;

    public bool IsPaid => Amount > 0;
}
