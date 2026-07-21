using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Model;

public record PunchPair
{
    public Punch? InPunch { get; set; } = null!;
    public Punch? OutPunch { get; set; } = null;
    public Position? Position { get; set; }
    public decimal? Rate { get; set; }
    public PayRule? AppliedRule { get; set; }

    /// <summary>True when this pair was produced by splitting an original pair at a PayRule boundary.</summary>
    public bool IsSplit { get; set; }

    public decimal TotalHours =>
        !IsMissingPunch
            ? (decimal)(OutPunch!.EffectiveTime - InPunch!.EffectiveTime).TotalHours
            : 0;

    public bool IsMissingPunch => InPunch is null || OutPunch is null;

    public bool HasInPunch => InPunch is not null;

    public bool HasOutPunch => OutPunch is not null;
}
