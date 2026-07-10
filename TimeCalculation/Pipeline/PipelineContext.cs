using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Carries all configuration required by pipeline stages 1–6.
/// Each stage calls GetRuleAt(instant) to resolve the correct PayRule for any point in time,
/// enabling effective-dated rule changes without running the pipeline multiple times.
/// </summary>
public class PipelineContext
{
    private readonly IReadOnlyList<PayRuleAssignment> _payRuleAssignments;
    private readonly IReadOnlyList<EmployeePositionAssignment> _positionAssignments;

    public Employee Employee { get; }
    public DateTimeZone EmployeeTimeZone { get; }

    public PipelineContext(
        Employee employee,
        IReadOnlyList<PayRuleAssignment> payRuleAssignments,
        IReadOnlyList<EmployeePositionAssignment> positionAssignments)
    {
        Employee = employee;
        EmployeeTimeZone = DateTimeZoneProviders.Tzdb[employee.HomeTimeZoneId];
        _payRuleAssignments = payRuleAssignments.OrderBy(a => a.EffectiveFrom).ToList();
        _positionAssignments = positionAssignments.OrderBy(a => a.EffectiveFrom).ToList();
    }

    /// <summary>Returns the PayRule active at the given instant in the employee's timezone.</summary>
    public PayRule GetRuleAt(Instant time)
    {
        var date = time.InZone(EmployeeTimeZone).Date;
        for (int i = _payRuleAssignments.Count - 1; i >= 0; i--)
        {
            var a = _payRuleAssignments[i];
            if (a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date))
                return a.PayRule;
        }
        throw new InvalidOperationException(
            $"No PayRule found for employee {Employee.Id} at {date}. " +
            "Ensure at least one PayRuleAssignment covers the calculation period.");
    }

    /// <summary>
    /// Returns the Position active at the given instant.
    /// If positionIdOverride is provided (from the punch), that position is used instead.
    /// Falls back to the employee's default position for the date, then null.
    /// </summary>
    public Position? GetPositionAt(Instant time, int? positionIdOverride = null)
    {
        if (positionIdOverride is not null)
        {
            for (int i = 0; i < _positionAssignments.Count; i++)
                if (_positionAssignments[i].Position.Id == positionIdOverride)
                    return _positionAssignments[i].Position;
            return null;
        }

        var date = time.InZone(EmployeeTimeZone).Date;
        for (int i = _positionAssignments.Count - 1; i >= 0; i--)
        {
            var a = _positionAssignments[i];
            if (a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date))
                return a.Position;
        }
        return null;
    }

    /// <summary>
    /// Returns the instants at which a PayRule or EmployeePosition effective-date boundary falls
    /// strictly between start and end. Used by Stage 3 to split pairs that span a rule or
    /// position/rate change, so multi-rate weighted calculations downstream never need a pair
    /// that straddles two different rules or positions.
    /// </summary>
    public IReadOnlyList<Instant> GetBoundaryInstantsBetween(Instant start, Instant end)
    {
        var startDate = start.InZone(EmployeeTimeZone).Date;
        var endDate = end.InZone(EmployeeTimeZone).Date;

        var boundaryDates = new SortedSet<LocalDate>();

        foreach (var a in _payRuleAssignments)
        {
            if (a.EffectiveFrom > startDate && a.EffectiveFrom <= endDate)
                boundaryDates.Add(a.EffectiveFrom);
        }

        foreach (var a in _positionAssignments)
        {
            if (a.EffectiveFrom > startDate && a.EffectiveFrom <= endDate)
                boundaryDates.Add(a.EffectiveFrom);
        }

        return boundaryDates
            .Select(d => d.AtMidnight().InZoneLeniently(EmployeeTimeZone).ToInstant())
            .ToList();
    }
}
