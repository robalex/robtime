namespace TimeCalculation.Model;

/// <summary>
/// Lifecycle state of a <see cref="PayRules.PayRule"/> version. Editing an Active rule never
/// mutates it in place — it creates a new row in the Draft state sharing the same RuleFamilyId;
/// promoting that Draft to Active supersedes whichever version of the family was previously Active.
/// </summary>
public enum PayRuleStatus
{
    Draft,
    Active,
    Superseded,
}
