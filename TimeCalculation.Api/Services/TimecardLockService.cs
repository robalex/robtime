using Microsoft.EntityFrameworkCore;
using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Whether a punch-mutating action would touch a period some employee's timecard has already been
/// approved for (UI_PLAN.md decision 21, Phase 6.7). One place all four punch-mutating paths call —
/// direct create/edit/delete (PunchService) and PunchChangeRequest submission alike — per §8's design
/// note that locking has to cover every write path, not just direct edits.
/// </summary>
public class TimecardLockService(PayrollDbContext db)
{
    /// <summary>Null when the period covering <paramref name="punchTime"/> (in the employee's own
    /// timezone) is open; a ready-to-return conflict message when it's locked.</summary>
    public async Task<string?> CheckAsync(Employee employee, Instant punchTime, CancellationToken ct)
    {
        var zone = DateTimeZoneProviders.Tzdb[employee.HomeTimeZoneId];
        var date = punchTime.InZone(zone).Date;

        var locked = await db.TimecardApprovals.AnyAsync(
            a => a.EmployeeId == employee.Id && a.UnapprovedAt == null
                 && a.PeriodStart <= date && date <= a.PeriodEnd, ct);

        return locked
            ? $"The pay period covering {date} is locked because this timecard has been approved. A supervisor must reopen it before this can change."
            : null;
    }
}
