namespace TimeCalculation.Model;

/// <summary>The four roles from UI_PLAN.md §5. SystemAdmin always scopes into one client at a time
/// (its own AppUser row has a null ClientId) rather than carrying a standing cross-client view.</summary>
public enum AppRole
{
    SystemAdmin,
    ClientAdmin,
    Supervisor,
    Employee,
}
