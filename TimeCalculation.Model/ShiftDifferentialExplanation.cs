using NodaTime;

namespace TimeCalculation.Model;

/// <summary>One shift's full set of DifferentialEvaluations — every DifferentialRule the client has,
/// evaluated against this shift, whether or not it applied. AnchorPunchId matches Shift.AnchorPunchId's
/// own identity scheme, so a UI can correlate this back to the same shift a PayResult/timecard would show.</summary>
public record ShiftDifferentialExplanation
{
    public required LocalDate ShiftDate { get; init; }
    public required int AnchorPunchId { get; init; }
    public required IReadOnlyList<DifferentialEvaluation> Evaluations { get; init; }
}
