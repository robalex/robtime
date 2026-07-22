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
/// Not yet consulted by the calculation pipeline — resolving "client override as of the
/// calculation date, else the rule's own built-in default" is deliberately deferred to a later
/// pass; the engine's waiver evaluation currently takes a bare <see cref="WaiverPolicy"/> supplied
/// by its caller, not yet by resolving this entity.
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
}
