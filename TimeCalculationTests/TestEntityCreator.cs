using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;

namespace TimeCalculationTests;

public static class TestEntityCreator
{
    public static Punch CreateTestPunch(Instant punchTime, PunchKind kind, Employee employee,
        string timeZoneId = "UTC")
    {
        return new Punch
        {
            PunchTime = punchTime,
            Kind = kind,
            EmployeeId = employee.Id,
            Employee = employee,
            PunchTimeZoneId = timeZoneId,
            CreatedAt = punchTime,
            CreatedBy = "test",
        };
    }

    public static PunchPair CreateTestPunchPair(Punch? inPunch, Punch? outPunch)
    {
        return new PunchPair
        {
            InPunch = inPunch!,
            OutPunch = outPunch,
        };
    }

    /// <summary>
    /// Creates a PipelineContext with a single PayRule that is effective for all time,
    /// and no position assignments.  Suitable for tests that only need a resolved rule.
    /// </summary>
    public static PipelineContext CreateContext(
        PayRule? payRule = null,
        Employee? employee = null,
        string timeZoneId = "UTC",
        IReadOnlyList<EmployeePositionAssignment>? positions = null)
    {
        employee ??= new Employee { Id = 1, HomeTimeZoneId = timeZoneId, MinimumWage = 15m };
        payRule ??= new PayRule();
        var assignment = new PayRuleAssignment(payRule, new LocalDate(2000, 1, 1));
        return new PipelineContext(employee, [assignment], positions ?? []);
    }
}
