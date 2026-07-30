using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using TimeCalculation.Pipeline.Differentials;
using Xunit;

namespace TimeCalculationTests;

/// <summary>One test per DifferentialOutcome value — the explainer's whole job is distinguishing
/// these reasons correctly, so each gets its own scenario rather than being incidentally covered.</summary>
public class DifferentialExplainerTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private List<Punch> PunchesUtc(int startHour, int endHour, int day = 2)
    {
        var midnight = Instant.FromUtc(2023, 1, day, 0, 0);
        return
        [
            TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(startHour), PunchKind.In, _emp),
            TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(endHour), PunchKind.Out, _emp),
        ];
    }

    private PipelineContext Ctx(List<DifferentialRule> rules, IReadOnlySet<string>? activeCodes = null)
        => new(
            _emp,
            [new PayRuleAssignment(
                new PayRule { ActiveDifferentialCodes = (activeCodes ?? rules.Select(r => r.Code).ToHashSet()).ToHashSet() },
                new LocalDate(2000, 1, 1))],
            [],
            rules);

    [Fact]
    public void Applied_WhenRuleQualifiesCleanly()
    {
        var rule = new DifferentialRule
        {
            Code = "ALLDAY", DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m,
        };
        var punches = PunchesUtc(9, 17); // Monday Jan 2, 2023 — 8h worked

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.Applied, eval.Outcome);
        Assert.Equal(8m, eval.QualifyingHours);
        Assert.Equal(16m, eval.Amount);
        Assert.Single(eval.Segments);
    }

    [Fact]
    public void SupersededByExclusivityGroup_WhenAnotherRuleInGroupPaysMore()
    {
        var loser = new DifferentialRule
        {
            Code = "LOW", DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 1m, ExclusivityGroup = "G",
        };
        var winner = new DifferentialRule
        {
            Code = "HIGH", DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 5m, ExclusivityGroup = "G",
        };
        var punches = PunchesUtc(9, 17);

        var explanations = DifferentialExplainer.Explain(punches, Ctx([loser, winner]));

        var evaluations = Assert.Single(explanations).Evaluations;
        var loserEval = evaluations.Single(e => e.Code == "LOW");
        var winnerEval = evaluations.Single(e => e.Code == "HIGH");

        Assert.Equal(DifferentialOutcome.SupersededByExclusivityGroup, loserEval.Outcome);
        Assert.Equal("HIGH", loserEval.SupersededByCode);
        Assert.Equal(8m, loserEval.QualifyingHours); // still reports what it would have earned
        Assert.Equal(8m, loserEval.Amount);
        Assert.Equal(DifferentialOutcome.Applied, winnerEval.Outcome);
    }

    [Fact]
    public void BelowMinHoursInWindow_WhenQualifyingTimeIsShortOfTheThreshold()
    {
        var rule = new DifferentialRule
        {
            Code = "SHORT", DayScheduleMode = DayScheduleMode.EveryDay,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m,
            MinHoursInWindow = 8m,
        };
        var punches = PunchesUtc(9, 13); // only 4h worked

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.BelowMinHoursInWindow, eval.Outcome);
        Assert.Equal(4m, eval.QualifyingHours);
        Assert.Equal(0m, eval.Amount);
    }

    [Fact]
    public void BelowMinHoursInRange_WhenTheWholeOccurrenceFallsShort()
    {
        var rule = new DifferentialRule
        {
            Code = "WEEKEND", DayScheduleMode = DayScheduleMode.ConsecutiveDayRange,
            DayOfWeekRangeStart = IsoDayOfWeek.Friday, DayOfWeekRangeEnd = IsoDayOfWeek.Sunday,
            AdjustmentType = DifferentialAdjustmentType.FlatPerHour, AdjustmentValue = 2m,
            MinHoursInRange = 8m,
        };
        // Friday Jan 6, 2023 — only 4h worked inside the Fri-Sun occurrence.
        var punches = PunchesUtc(9, 13, day: 6);

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.BelowMinHoursInRange, eval.Outcome);
        Assert.Equal(4m, eval.QualifyingHours);
        Assert.Equal(0m, eval.Amount);
    }

    [Fact]
    public void NotActiveOnAnyWorkedDay_WhenTheDayScheduleNeverMatches()
    {
        var rule = new DifferentialRule
        {
            Code = "SUNONLY", DayScheduleMode = DayScheduleMode.DaysOfWeek,
            DaysOfWeek = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Sunday },
        };
        var punches = PunchesUtc(9, 17); // Monday Jan 2, 2023

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.NotActiveOnAnyWorkedDay, eval.Outcome);
    }

    [Fact]
    public void NoWindowOverlap_WhenTheDayIsActiveButTheWindowMisses()
    {
        var rule = new DifferentialRule
        {
            Code = "LATENIGHT", DayScheduleMode = DayScheduleMode.EveryDay,
            WindowStart = new LocalTime(22, 0), WindowEnd = new LocalTime(23, 0),
        };
        var punches = PunchesUtc(9, 17); // worked hours never touch 22:00-23:00

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.NoWindowOverlap, eval.Outcome);
    }

    [Fact]
    public void NotEnabledByPayRule_WhenActiveDifferentialCodesExcludesIt()
    {
        var rule = new DifferentialRule { Code = "OFF", DayScheduleMode = DayScheduleMode.EveryDay };
        var punches = PunchesUtc(9, 17);

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule], activeCodes: new HashSet<string>()));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.NotEnabledByPayRule, eval.Outcome);
    }

    [Fact]
    public void ShiftHasMissingPunches_WhenAnOrphanPunchExists()
    {
        var rule = new DifferentialRule { Code = "ALLDAY", DayScheduleMode = DayScheduleMode.EveryDay };
        var midnight = Instant.FromUtc(2023, 1, 2, 0, 0);
        var punches = new List<Punch> { TestEntityCreator.CreateTestPunch(midnight + Duration.FromHours(9), PunchKind.In, _emp) };

        var explanations = DifferentialExplainer.Explain(punches, Ctx([rule]));

        var eval = Assert.Single(Assert.Single(explanations).Evaluations);
        Assert.Equal(DifferentialOutcome.ShiftHasMissingPunches, eval.Outcome);
    }
}
