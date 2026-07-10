using TimeCalculation.Model.Premiums;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>Decides whether a detected violation is waived given a policy and the overrides present.</summary>
public static class WaiverEvaluator
{
    public static bool IsWaived(WaiverPolicy policy, IReadOnlyList<OverrideKind> overrides)
    {
        bool sup = overrides.Contains(OverrideKind.SupervisorApproval);
        bool emp = overrides.Contains(OverrideKind.EmployeeWaiver);

        return policy switch
        {
            WaiverPolicy.NotWaivable => false,
            WaiverPolicy.SupervisorOnly => sup,
            WaiverPolicy.EmployeeOnly => emp,
            WaiverPolicy.BothRequired => sup && emp,
            _ => false,
        };
    }
}
