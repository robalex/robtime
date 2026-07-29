namespace TimeCalculation.Model;

/// <summary>What a PunchChangeRequest proposes. Determines both which Requested* fields on the
/// request are meaningful and what approval actually does to the Punch table.</summary>
public enum PunchChangeKind
{
    /// <summary>Propose a brand-new punch that doesn't exist yet — PunchId is null until approval
    /// creates one and backfills it.</summary>
    Add,

    /// <summary>Propose changing an existing punch's fields — PunchId required, Requested* fields
    /// use the same partial-patch semantics as UpdatePunchRequest.</summary>
    Edit,

    /// <summary>Propose soft-deleting an existing punch — PunchId required, Requested* fields
    /// ignored.</summary>
    Delete,
}
