using NodaTime;
using TimeCalculation.Calculation.Overtime;
using TimeCalculation.Model;
using TimeCalculationTests.Oracles;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Differential tests pitting <see cref="FederalOvertimeRule"/> and <see cref="CaliforniaOvertimeRule"/>
/// against <see cref="NaiveOvertimeAllocator"/>. Complements <c>OvertimeRulesTests</c>, which pins
/// specific hand-computed allocations: these hunt the combinations nobody writes by hand — a week
/// that trips daily OT, doubletime, the seventh-day override *and* the weekly threshold at once.
/// </summary>
public class OvertimeRulesOracleTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private static readonly LocalDate WeekStart = new(2023, 1, 1);

    private WorkDay Day(LocalDate date, decimal hours, int consecutive)
    {
        var start = Instant.FromUtc(2023, 1, 2, 8, 0);
        var inPunch = TestEntityCreator.CreateTestPunch(start, PunchKind.In, _emp);
        var outPunch = TestEntityCreator.CreateTestPunch(
            start + Duration.FromHours((double)hours), PunchKind.Out, _emp);
        var shift = new Shift { PunchPairs = [new PunchPair { InPunch = inPunch, OutPunch = outPunch, Rate = 20m }] };
        return new WorkDay { Date = date, Shifts = [shift], ConsecutiveDayNumber = consecutive };
    }

    /// <summary>A week of 0–7 worked days on the quarter-hour grid the naive walk requires. Day
    /// lengths reach past 12 so doubletime is genuinely exercised, and a run of seven consecutive
    /// days shows up often enough to hit the seventh-day override.</summary>
    private Workweek GenerateWeek(Random rng)
    {
        var days = new List<WorkDay>();
        int workedDays = rng.Next(0, 8);

        for (int i = 0; i < workedDays; i++)
        {
            // Quarter-hour steps from 0.25 up to 16 hours.
            decimal hours = rng.Next(1, 65) * 0.25m;
            days.Add(Day(WeekStart.PlusDays(i), hours, i + 1));
        }

        return new Workweek { StartDate = WeekStart, Days = days };
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void Federal_MatchesNaiveOracle(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 200; i++)
        {
            var week = GenerateWeek(rng);
            decimal weekly = rng.Next(140, 181) * 0.25m;   // 35h–45h

            var actual = new FederalOvertimeRule(weekly).Allocate(week);
            var expected = NaiveOvertimeAllocator.Allocate(week, NaiveOvertimeConfig.Federal(weekly));

            AssertAgrees(expected, actual, week, $"Federal weekly={weekly}");
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(42)] [InlineData(99)] [InlineData(2024)] [InlineData(31337)]
    public void California_MatchesNaiveOracle_AcrossThresholdAndToggleCombinations(int seed)
    {
        var rng = new Random(seed);

        for (int i = 0; i < 200; i++)
        {
            var week = GenerateWeek(rng);
            var config = new NaiveOvertimeConfig
            {
                DailyOvertimeThreshold = rng.Next(24, 41) * 0.25m,    // 6h–10h
                DailyDoubletimeThreshold = rng.Next(40, 57) * 0.25m,  // 10h–14h
                WeeklyThreshold = rng.Next(140, 181) * 0.25m,         // 35h–45h
                ApplyDaily = rng.Next(2) == 0,
                ApplySeventhDay = rng.Next(2) == 0,
            };

            var actual = new CaliforniaOvertimeRule(
                config.DailyOvertimeThreshold,
                config.DailyDoubletimeThreshold,
                config.WeeklyThreshold,
                config.ApplyDaily,
                config.ApplySeventhDay).Allocate(week);

            var expected = NaiveOvertimeAllocator.Allocate(week, config);

            AssertAgrees(expected, actual, week, Describe(config));
        }
    }

    [Fact]
    public void California_WithDailyAndSeventhDayOff_DegeneratesToFederal()
    {
        var rng = new Random(4242);

        for (int i = 0; i < 200; i++)
        {
            var week = GenerateWeek(rng);

            var federal = new FederalOvertimeRule().Allocate(week);
            var california = new CaliforniaOvertimeRule(applyDaily: false, applySeventhDay: false).Allocate(week);

            Assert.True(
                federal == california,
                $"With daily and seventh-day rules off, California must equal Federal, but " +
                $"{Describe(california)} != {Describe(federal)} for a {week.TotalHours}h week.");
        }
    }

    private static void AssertAgrees(
        OvertimeAllocation expected, OvertimeAllocation actual, Workweek week, string context)
    {
        Assert.True(
            expected == actual,
            $"{context}, days=[{string.Join(", ", week.Days.Select(d => $"{d.TotalHours}h#{d.ConsecutiveDayNumber}"))}]: " +
            $"production returned {Describe(actual)}, oracle returned {Describe(expected)}");

        // Conservation: the three buckets are documented as mutually exclusive and summing to hours
        // worked. Needs no oracle, and would catch both implementations dropping the same hour.
        Assert.True(
            actual.TotalHours == week.TotalHours,
            $"{context}: allocated {actual.TotalHours}h but the week holds {week.TotalHours}h.");

        Assert.True(
            actual.RegularHours >= 0m && actual.OvertimeHours >= 0m && actual.DoubletimeHours >= 0m,
            $"{context}: negative bucket in {Describe(actual)}.");
    }

    private static string Describe(OvertimeAllocation allocation)
        => $"(reg {allocation.RegularHours}, ot {allocation.OvertimeHours}, dt {allocation.DoubletimeHours})";

    private static string Describe(NaiveOvertimeConfig config)
        => $"California dailyOt={config.DailyOvertimeThreshold} dailyDt={config.DailyDoubletimeThreshold} " +
           $"weekly={config.WeeklyThreshold} applyDaily={config.ApplyDaily} applySeventh={config.ApplySeventhDay}";
}
