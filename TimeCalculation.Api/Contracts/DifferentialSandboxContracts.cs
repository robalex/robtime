using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// The differential sandbox: given a real employee, the pay rule to evaluate under, and a date
/// window, projects every DifferentialRule the pay rule actually enables (PayRule.
/// ActiveDifferentialCodes) over that window as zones — independent of any punches — so the Setup UI
/// can render them as a weekly calendar. TestPunches, when given, are additionally run through the
/// real engine (DifferentialExplainer) so the response's Shifts show exactly which differentials
/// applied to them and why — never persisted.
/// </summary>
public sealed record DifferentialSandboxRequest
{
    public required int EmployeeId { get; init; }
    public required int PayRuleId { get; init; }
    public int? HolidayCalendarId { get; init; }
    public required LocalDate WindowStart { get; init; }
    public required int DayCount { get; init; }
    public List<SandboxTestPunch> TestPunches { get; init; } = [];
}

/// <summary>A test punch entered as local wall-clock time + zone, resolved through the same
/// LocalTimeResolver punch import and manual entry already use — DST gaps/ambiguity behave
/// identically here. Never persisted.</summary>
public sealed record SandboxTestPunch
{
    public required LocalDateTime PunchTime { get; init; }
    public string? PunchTimeZoneId { get; init; }
    public bool? DaylightSaving { get; init; }
    public required PunchKind Kind { get; init; }
}

public sealed record DifferentialZoneResponse
{
    public required string Code { get; init; }
    public required Instant Start { get; init; }
    public required Instant End { get; init; }

    public static DifferentialZoneResponse FromDomain(DifferentialZone zone) => new()
    {
        Code = zone.Code,
        Start = zone.Start,
        End = zone.End,
    };
}

public sealed record QualifyingSegmentResponse
{
    public required Instant Start { get; init; }
    public required Instant End { get; init; }

    public static QualifyingSegmentResponse FromDomain(QualifyingSegment segment) => new()
    {
        Start = segment.Start,
        End = segment.End,
    };
}

/// <summary>One DifferentialRule's verdict against one test shift — Outcome says why it did or
/// didn't apply; QualifyingHours/Amount/Segments are populated whenever qualifying time was found at
/// all, even for a losing outcome like SupersededByExclusivityGroup, so the UI can show "this would
/// have earned $X" alongside the reason it didn't.</summary>
public sealed record DifferentialEvaluationResponse
{
    public required string Code { get; init; }
    public required DifferentialOutcome Outcome { get; init; }
    public required decimal QualifyingHours { get; init; }
    public required decimal Amount { get; init; }
    public required List<QualifyingSegmentResponse> Segments { get; init; }
    public string? SupersededByCode { get; init; }
    public required string Explanation { get; init; }

    public static DifferentialEvaluationResponse FromDomain(DifferentialEvaluation evaluation) => new()
    {
        Code = evaluation.Code,
        Outcome = evaluation.Outcome,
        QualifyingHours = evaluation.QualifyingHours,
        Amount = evaluation.Amount,
        Segments = evaluation.Segments.Select(QualifyingSegmentResponse.FromDomain).ToList(),
        SupersededByCode = evaluation.SupersededByCode,
        Explanation = evaluation.Explanation,
    };
}

public sealed record ShiftDifferentialExplanationResponse
{
    public required LocalDate ShiftDate { get; init; }
    public required int AnchorPunchId { get; init; }
    public required List<DifferentialEvaluationResponse> Evaluations { get; init; }

    public static ShiftDifferentialExplanationResponse FromDomain(ShiftDifferentialExplanation explanation) => new()
    {
        ShiftDate = explanation.ShiftDate,
        AnchorPunchId = explanation.AnchorPunchId,
        Evaluations = explanation.Evaluations.Select(DifferentialEvaluationResponse.FromDomain).ToList(),
    };
}

public sealed record DifferentialSandboxResponse
{
    public required LocalDate WindowStart { get; init; }
    public required int DayCount { get; init; }
    public required List<DifferentialZoneResponse> Zones { get; init; }
    public required List<ShiftDifferentialExplanationResponse> Shifts { get; init; }
}
