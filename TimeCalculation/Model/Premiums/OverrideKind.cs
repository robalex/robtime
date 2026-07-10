namespace TimeCalculation.Model.Premiums;

/// <summary>An override attached to a specific premium occurrence (a shift's meal/rest violation).</summary>
public enum OverrideKind
{
    SupervisorApproval,   // a supervisor attests the premium should not apply
    EmployeeWaiver,       // the employee waived the break
}
