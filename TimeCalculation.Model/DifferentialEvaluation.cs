namespace TimeCalculation.Model;

/// <summary>
/// One DifferentialRule's full evaluation against one shift, produced by DifferentialExplainer for
/// the sandbox's "why did/didn't this apply" panel. QualifyingHours/Amount/Segments are populated
/// whenever qualifying time was found at all (even if the rule ultimately didn't apply, e.g.
/// SupersededByExclusivityGroup) so the UI can show "this would have earned $X" alongside the reason
/// it didn't.
/// </summary>
public record DifferentialEvaluation
{
    public required string Code { get; init; }
    public required DifferentialOutcome Outcome { get; init; }
    public required decimal QualifyingHours { get; init; }
    public required decimal Amount { get; init; }
    public required IReadOnlyList<QualifyingSegment> Segments { get; init; }

    /// <summary>Only set when Outcome == SupersededByExclusivityGroup — the code of the rule that won.</summary>
    public string? SupersededByCode { get; init; }

    public required string Explanation { get; init; }
}
