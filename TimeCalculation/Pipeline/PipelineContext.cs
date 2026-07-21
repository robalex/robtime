using System.Runtime.InteropServices;
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
    // List<T>, not IReadOnlyList<T>, specifically so CollectionsMarshal.AsSpan can hand the
    // framework's own binary search the backing array with no copy.
    private readonly List<PayRuleAssignment> _payRuleAssignments;
    private readonly List<EmployeePositionAssignment> _positionAssignments;

    public Employee Employee { get; }
    public DateTimeZone EmployeeTimeZone { get; }
    public IReadOnlyList<DifferentialRule> DifferentialRules { get; }
    public HolidayCalendar? HolidayCalendar { get; }

    public PipelineContext(
        Employee employee,
        IReadOnlyList<PayRuleAssignment> payRuleAssignments,
        IReadOnlyList<EmployeePositionAssignment> positionAssignments,
        IReadOnlyList<DifferentialRule>? differentialRules = null,
        HolidayCalendar? holidayCalendar = null)
    {
        Employee = employee;
        EmployeeTimeZone = DateTimeZoneProviders.Tzdb[employee.HomeTimeZoneId];
        _payRuleAssignments = payRuleAssignments.OrderBy(a => a.EffectiveFrom).ToList();
        _positionAssignments = positionAssignments.OrderBy(a => a.EffectiveFrom).ToList();
        DifferentialRules = differentialRules ?? [];
        HolidayCalendar = holidayCalendar;

        ValidateDifferentialRules(DifferentialRules);
    }

    // A ConsecutiveDayRange must span at least two distinct weekdays. A single-day "range" is
    // semantically a DaysOfWeek selection of one day, and it's the only shape that lets a range
    // occurrence's [start, end) span invert (a single day plus a midnight-wrapping window), so
    // rejecting it here lets ContinuousRangeQualifyingHoursCalculator assume start < end.
    private static void ValidateDifferentialRules(IReadOnlyList<DifferentialRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange
                && rule.DayOfWeekRangeStart == rule.DayOfWeekRangeEnd)
            {
                throw new ArgumentException(
                    $"DifferentialRule '{rule.Code}' uses ConsecutiveDayRange but its range start and " +
                    $"end are the same day ({rule.DayOfWeekRangeStart}). A single-day selection should " +
                    "use DayScheduleMode.DaysOfWeek instead.");
            }
        }
    }

    /// <summary>
    /// Returns the PayRule active at the given instant in the employee's timezone.
    /// Throws if no assignment covers the date — a coverage gap is invalid input for the pure
    /// pipeline stages, which all call this. Batch/orchestration code that wants to detect and
    /// skip an employee with a gap instead of failing the whole run should call
    /// <see cref="TryGetRuleAt"/> first.
    /// </summary>
    public PayRule GetRuleAt(Instant time)
    {
        if (TryGetRuleAt(time, out var rule))
            return rule;

        var date = time.InZone(EmployeeTimeZone).Date;
        throw new InvalidOperationException(
            $"No PayRule found for employee {Employee.Id} at {date}. " +
            "Ensure at least one PayRuleAssignment covers the calculation period.");
    }

    /// <summary>Non-throwing probe for whether a PayRule covers the given instant.</summary>
    public bool TryGetRuleAt(Instant time, out PayRule rule)
    {
        var date = time.InZone(EmployeeTimeZone).Date;
        var found = FindEffective(_payRuleAssignments, date, a => a.EffectiveFrom, a => a.EffectiveTo);
        if (found is not null)
        {
            rule = found.PayRule;
            return true;
        }
        rule = null!;
        return false;
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
        return FindEffective(_positionAssignments, date, a => a.EffectiveFrom, a => a.EffectiveTo)?.Position;
    }

    /// <summary>
    /// Finds the effective-dated element covering <paramref name="date"/> in a list sorted
    /// ascending by EffectiveFrom, preferring the element with the greatest EffectiveFrom that
    /// covers the date (matching the semantics of a plain reverse linear scan, just in
    /// O(log n) for the common non-overlapping case instead of always O(n)).
    ///
    /// Uses the framework's own <see cref="MemoryExtensions.BinarySearch{T,TComparable}"/> (a
    /// generic, allocation-free search by a custom IComparable key, not just an exact T value) to
    /// find the rightmost index whose EffectiveFrom &lt;= date, rather than hand-rolling the
    /// lo/hi/mid loop. BinarySearch only guarantees returning SOME index equal to the key when
    /// duplicates exist, not which one, so an exact match is followed by a short forward scan to
    /// the true rightmost tie — every index earlier than that also satisfies EffectiveFrom &lt;=
    /// date (the list is sorted), so walking backward from there and returning the first whose
    /// EffectiveTo covers the date reproduces the original full scan exactly. The walk-back only
    /// visits more than one candidate when assignments overlap — same worst case as before, but
    /// the typical contiguous-assignment case is a single check after the search.
    /// </summary>
    private static T? FindEffective<T>(
        List<T> sorted, LocalDate date, Func<T, LocalDate> from, Func<T, LocalDate?> to)
        where T : class
    {
        int found = CollectionsMarshal.AsSpan(sorted).BinarySearch(new EffectiveFromKey<T>(date, from));

        int rightmost;
        if (found >= 0)
        {
            rightmost = found;
            while (rightmost + 1 < sorted.Count && from(sorted[rightmost + 1]) == date)
                rightmost++;
        }
        else
        {
            rightmost = ~found - 1;   // ~found = index of the first element with EffectiveFrom > date
        }

        for (int i = rightmost; i >= 0; i--)
        {
            var item = sorted[i];
            var effectiveTo = to(item);
            if (effectiveTo is null || effectiveTo >= date)
                return item;
        }
        return null;
    }

    /// <summary>Search key for <see cref="MemoryExtensions.BinarySearch{T,TComparable}"/>: compares
    /// a target date against a candidate's projected EffectiveFrom, without needing a dummy T instance.</summary>
    private readonly struct EffectiveFromKey<T>(LocalDate date, Func<T, LocalDate> from) : IComparable<T>
    {
        public int CompareTo(T? other) => date.CompareTo(from(other!));
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
