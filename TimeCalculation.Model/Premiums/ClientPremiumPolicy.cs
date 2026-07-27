using NodaTime;

namespace TimeCalculation.Model.Premiums;

/// <summary>
/// A client's own determination of whether a specific premium can be waived, overriding that
/// premium rule's own hardcoded default (each rule class in the calculation engine has its own
/// built-in <see cref="WaiverPolicy"/>). RobTime never asserts an unverified legal answer on the
/// client's behalf; this is what the client explicitly attested to, audited
/// (<see cref="SetBy"/>/<see cref="SetAt"/>) and effective-dated so a past calculation stays
/// reproducible even after the policy is changed later.
///
/// Resolved by <see cref="TimeCalculation.Pipeline.PipelineContext.GetWaiverPolicyOverridesAt"/> as
/// of the shift's date and consulted by <c>PremiumRuleBase.Resolve</c> — a client override wins over
/// the rule's own built-in default when one is effective for that premium code on that date.
/// </summary>
public class ClientPremiumPolicy
{
    public int Id { get; set; }
    public int ClientId { get; set; }

    /// <summary>Matches an <c>IPremiumRule.Code</c> (e.g. "PR_MEAL"). Not a foreign key — premium
    /// rules are code-registered classes (see PremiumRegistry), not database rows.</summary>
    public string PremiumCode { get; set; } = string.Empty;

    public WaiverPolicy WaiverPolicy { get; set; }

    public string SetBy { get; set; } = string.Empty;
    public Instant SetAt { get; set; }

    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }

    /// <summary>Optional free-text note (e.g. a citation) the client can attach to their determination.</summary>
    public string? Justification { get; set; }

    public bool IsDeleted { get; set; }
}
