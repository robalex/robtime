namespace TimeCalculation.Model.Premiums;

/// <summary>What combination of overrides (if any) waives a premium.</summary>
public enum WaiverPolicy
{
    NotWaivable,     // statutory penalty that cannot be waived (e.g. CA/CO rest)
    SupervisorOnly,
    EmployeeOnly,
    BothRequired,    // supervisor AND employee (e.g. CA meal)
}
