using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api;

/// <summary>
/// Local-dev convenience only — populates an otherwise-empty database with enough shaped data to
/// actually build/demo the configuration UI against (UI_PLAN.md Phase 0: "you cannot build or demo
/// a configuration UI against an empty database"). Run via `dotnet run -- --seed`, which exits
/// immediately after seeding rather than starting the web host (see Program.cs). Not idempotent —
/// re-running against a database that already has this data creates a second copy; it's meant for
/// a freshly-migrated empty database, not a repeatable fixture loader.
/// </summary>
public static class DevSeeder
{
    private static readonly string[] FirstNames =
        ["Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Jamie", "Avery", "Quinn", "Drew", "Sam", "Reese"];
    private static readonly string[] LastNames =
        ["Nguyen", "Garcia", "Smith", "Patel", "Kim", "Johnson", "Rossi", "Okafor", "Muller", "Sato", "Brown", "Lopez"];

    public static async Task SeedAsync(PayrollDbContext db, IClock clock, CancellationToken ct)
    {
        var today = clock.GetCurrentInstant().InUtc().Date;

        var client = new Client
        {
            Name = "Acme Diner Co.",
            CreatedBy = "dev-seed",
            CreatedDate = clock.GetCurrentInstant().ToDateTimeUtc(),
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        var positions = new[]
        {
            new Position { ClientId = client.Id, Code = "SERVER", Name = "Server", BaseRate = 15.00m },
            new Position { ClientId = client.Id, Code = "COOK", Name = "Cook", BaseRate = 19.50m },
            new Position { ClientId = client.Id, Code = "DISH", Name = "Dishwasher", BaseRate = 16.00m },
            new Position { ClientId = client.Id, Code = "MGR", Name = "Shift Manager", BaseRate = 24.00m },
        };
        db.Positions.AddRange(positions);
        await db.SaveChangesAsync(ct);

        // Two pay rules on different templates, both already Active (unlike a freshly-created
        // Draft via the API) — seed data should look like an already-configured client, not one
        // still mid-setup. RuleFamilyId set via the same two-phase save PayRuleService uses.
        var federalRule = new PayRule
        {
            ClientId = client.Id,
            Name = "Federal Standard",
            TemplateCode = "federal-standard",
            Status = PayRuleStatus.Active,
            EffectiveFrom = today.PlusMonths(-6),
        };
        var californiaRule = new PayRule
        {
            ClientId = client.Id,
            Name = "California",
            TemplateCode = "california",
            Status = PayRuleStatus.Active,
            EffectiveFrom = today.PlusMonths(-6),
            ShiftDateStrategy = ShiftDateStrategy.FirstPunchLocalDate,
            OvertimeRule = { HasDailyOvertime = true, HasSeventhDayRule = true },
        };
        db.PayRules.AddRange(federalRule, californiaRule);
        await db.SaveChangesAsync(ct);
        federalRule.RuleFamilyId = federalRule.Id;
        californiaRule.RuleFamilyId = californiaRule.Id;
        await db.SaveChangesAsync(ct);

        var employees = new List<Employee>();
        for (var i = 0; i < 12; i++)
        {
            employees.Add(new Employee
            {
                ClientId = client.Id,
                FirstName = FirstNames[i % FirstNames.Length],
                LastName = LastNames[i % LastNames.Length],
                MinimumWage = 15.00m,
                HomeTimeZoneId = "America/Los_Angeles",
                State = i % 3 == 0 ? "CA" : "WA",
            });
        }
        db.Employees.AddRange(employees);
        await db.SaveChangesAsync(ct);

        var assignmentStart = today.PlusMonths(-3);
        foreach (var (employee, index) in employees.Select((e, i) => (e, i)))
        {
            var position = positions[index % positions.Length];
            var payRule = employee.State == "CA" ? californiaRule : federalRule;

            db.EmployeePositionAssignments.Add(new EmployeePositionAssignmentEntity
            {
                ClientId = client.Id,
                EmployeeId = employee.Id,
                PositionId = position.Id,
                EffectiveFrom = assignmentStart,
            });
            db.PayRuleAssignments.Add(new PayRuleAssignmentEntity
            {
                ClientId = client.Id,
                EmployeeId = employee.Id,
                PayRuleId = payRule.Id,
                EffectiveFrom = assignmentStart,
            });
        }
        await db.SaveChangesAsync(ct);

        // A few weeks of simple weekday punches for a handful of employees — enough to see real
        // rows in the UI, not an attempt at pattern coverage (that's EndToEndTests'/
        // RecordedScenarioTests' job in the engine's own test suite, already exhaustive).
        var punches = new List<Punch>();
        foreach (var employee in employees.Take(5))
        {
            for (var daysAgo = 13; daysAgo >= 0; daysAgo--)
            {
                var date = today.PlusDays(-daysAgo);
                if (date.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
                {
                    continue;
                }

                var clockIn = date.At(new LocalTime(9, 0)).InZoneStrictly(DateTimeZoneProviders.Tzdb["America/Los_Angeles"]).ToInstant();
                var clockOut = date.At(new LocalTime(17, 0)).InZoneStrictly(DateTimeZoneProviders.Tzdb["America/Los_Angeles"]).ToInstant();

                punches.Add(new Punch
                {
                    ClientId = client.Id,
                    EmployeeId = employee.Id,
                    PunchTime = clockIn,
                    PunchTimeZoneId = "America/Los_Angeles",
                    Kind = PunchKind.In,
                    CreatedAt = clock.GetCurrentInstant(),
                    CreatedBy = "dev-seed",
                });
                punches.Add(new Punch
                {
                    ClientId = client.Id,
                    EmployeeId = employee.Id,
                    PunchTime = clockOut,
                    PunchTimeZoneId = "America/Los_Angeles",
                    Kind = PunchKind.Out,
                    CreatedAt = clock.GetCurrentInstant(),
                    CreatedBy = "dev-seed",
                });
            }
        }
        db.Punches.AddRange(punches);
        await db.SaveChangesAsync(ct);

        Console.WriteLine(
            $"Seeded: 1 client, {positions.Length} positions, 2 pay rules, {employees.Count} employees, " +
            $"{punches.Count} punches.");
    }
}
