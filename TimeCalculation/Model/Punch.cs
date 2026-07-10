using NodaTime;

namespace TimeCalculation.Model;

public record Punch
{
    public int Id { get; init; }
    public int EmployeeId { get; init; }
    public Instant PunchTime { get; init; }
    public Instant? RoundedPunchTime { get; init; }
    public string PunchTimeZoneId { get; init; } = "UTC";
    public PunchKind Kind { get; init; }
    public PunchSubtype? Subtype { get; init; }   // null = infer in Stage 2; non-null = forced
    public int? PositionId { get; init; }
    public decimal? Amount { get; init; }   // FixedDollar amount
    public decimal? Hours { get; init; }    // FixedHours quantity
    public BonusKind? BonusKind { get; init; }   // on FixedDollar bonus punches; drives RROP inclusion
    public Instant CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }

    // Navigation properties — may be null in pure pipeline contexts
    public Employee? Employee { get; init; }
    public Position? Position { get; init; }

    /// <summary>Rounded time when available; raw punch time otherwise.</summary>
    public Instant EffectiveTime => RoundedPunchTime ?? PunchTime;

    public bool IsClockPunch => Kind is PunchKind.In or PunchKind.Out;
    public bool IsFixedEntry => Kind is PunchKind.FixedDollar or PunchKind.FixedHours;
}
