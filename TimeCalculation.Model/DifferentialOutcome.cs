namespace TimeCalculation.Model;

/// <summary>Why a DifferentialRule did or didn't apply to a shift — the sandbox explainer's verdict
/// for one (rule, shift) pair. Ordered roughly by how early in evaluation each is decided.</summary>
public enum DifferentialOutcome
{
    /// <summary>The rule's own worked-hours threshold was met, it won any exclusivity group it's in,
    /// and (for a ConsecutiveDayRange rule with MinHoursInRange) its whole occurrence qualified.</summary>
    Applied,

    /// <summary>Qualified on its own, but lost to a higher-amount rule in the same
    /// DifferentialRule.ExclusivityGroup.</summary>
    SupersededByExclusivityGroup,

    /// <summary>Some qualifying time overlapped, but less than DifferentialRule.MinHoursInWindow.</summary>
    BelowMinHoursInWindow,

    /// <summary>Qualified on this shift, but the ConsecutiveDayRange occurrence it belongs to didn't
    /// sum to DifferentialRule.MinHoursInRange across every shift in it.</summary>
    BelowMinHoursInRange,

    /// <summary>The rule's day schedule (DayScheduleMode) was never active on any day this shift
    /// worked — no time-of-day window could have mattered.</summary>
    NotActiveOnAnyWorkedDay,

    /// <summary>The rule was active on a day this shift worked, but none of the worked time fell
    /// inside its time-of-day window.</summary>
    NoWindowOverlap,

    /// <summary>The PayRule in effect for this shift doesn't list this rule's Code in
    /// ActiveDifferentialCodes — the most common reason a newly-created rule "does nothing."</summary>
    NotEnabledByPayRule,

    /// <summary>The shift has an orphan In-only or Out-only pair — no real worked interval exists to
    /// evaluate against any rule.</summary>
    ShiftHasMissingPunches,
}
