using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Persistence;

/// <summary>
/// Persistence-shape entities for the effective-dated assignment tables.  The pure-domain records
/// (PayRuleAssignment, EmployeePositionAssignment) are immutable value types the calculator consumes;
/// these mutable POCOs are what EF Core maps, keeping the domain free of persistence concerns.
/// </summary>
public class PayRuleAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PayRuleId { get; set; }
    public PayRule PayRule { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }

    public PayRuleAssignment ToDomain() => new(PayRule, EffectiveFrom, EffectiveTo);
}

public class EmployeePositionAssignmentEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int EmployeeId { get; set; }
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public LocalDate EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }
    public decimal? Rate { get; set; }

    public EmployeePositionAssignment ToDomain() => new(Position, EffectiveFrom, EffectiveTo, Rate);
}

/// <summary>
/// Thin profile/authorization row — NOT a credential store. Amazon Cognito owns passwords, MFA, and
/// password reset (UI_PLAN.md §5); this table exists only because the API still needs ClientId/
/// EmployeeId/Role for FK targets and tenant-filtered listing. Keyed by the Cognito `sub` claim
/// (stable across the user's lifetime, survives SSO federation later) rather than a synthetic int —
/// there's no join-performance reason to prefer an int surrogate at this table's size, and using
/// `sub` directly means resolving "who is this request" never needs a lookup: it's already the JWT.
/// </summary>
public class AppUser
{
    public string CognitoSub { get; set; } = string.Empty;

    /// <summary>Null only for SystemAdmin — see AppRole's doc comment.</summary>
    public int? ClientId { get; set; }

    /// <summary>Set when this user IS an employee (self-service access); null for admin/supervisor
    /// accounts with no corresponding Employee row.</summary>
    public int? EmployeeId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public AppRole Role { get; set; }
}
