using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

/// <summary>
/// Pay scenarios pinned to 29 CFR Part 778 — the Wage and Hour Division's interpretive bulletin on
/// FLSA overtime. Unlike the rest of the suite, whose expected values are hand-computed by us, the
/// §778.110 figures below are the regulator's own worked examples. That makes them the closest thing
/// to an authoritative oracle this engine has: if one of these moves, the engine no longer agrees
/// with the agency that enforces the statute.
///
/// PROVENANCE — read before adding to this file. Two different kinds of test live here, and
/// conflating them would defeat the purpose:
///
///   • §778.110(a)/(b) carry numeric examples IN THE REGULATION ITSELF. Every figure asserted below
///     ($12 rate, 46 hours, $46 bonus, $13 recalculated rate, $588 and $637 totals) is taken from
///     the regulation text, retrieved 2026-08-03, not computed by us. Do not "fix" one of these to
///     make it pass — a failure here means the engine is wrong, or the regulation was amended.
///
///   • §778.115 (two or more rates) states its METHOD in prose — "the weighted average of such
///     rates... total earnings... divided by the total number of hours worked at all jobs" — but
///     contains NO worked example. The figures in that test are ours, chosen to exercise the
///     regulation's stated method. It is a normal hand-computed scenario wearing a citation, and
///     carries no more authority than any other test in the suite. Same caveat for §778.209
///     (bonus inclusion), which likewise states a method without numbers.
///
/// A presentation note on §778.110(a): the regulation totals the week as 40 hours at the straight
/// rate plus 6 at time-and-a-half. This engine uses the FLSA "premium" representation instead — all
/// 46 hours at straight time, plus a half-rate premium on the 6 overtime hours (see PayResult's doc
/// comment for why: differentials and bonuses would otherwise be double-counted). The two
/// decompositions are arithmetically identical and both land on $588; only GrossPay is compared, not
/// the intermediate split.
/// </summary>
public class CfrPart778ScenarioTests
{
    // Well below every rate used here, so no minimum-wage floor can interfere with the figures.
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 7.25m };

    private Punch In(int day, int hour, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, day, hour, 0), PunchKind.In, _emp)
            with { PositionId = positionId };

    private Punch Out(int day, int hour, int? positionId = null) =>
        TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, day, hour, 0), PunchKind.Out, _emp)
            with { PositionId = positionId };

    private PipelineContext Ctx(params Position[] positions)
    {
        var assignments = positions
            .Select(p => new EmployeePositionAssignment(p, new LocalDate(2000, 1, 1)))
            .ToList();

        // Default PayRule is plain federal: 40-hour weekly threshold, no daily overtime, no
        // premiums or differentials — matching the bare statutory case Part 778 describes.
        return new PipelineContext(
            _emp, [new PayRuleAssignment(new PayRule(), new LocalDate(2000, 1, 1))], assignments);
    }

    /// <summary>46 hours inside the Jan 1–7 2023 workweek (WorkweekStartDay defaults to Sunday):
    /// nine hours Monday through Friday, one more on Saturday.</summary>
    private List<Punch> FortySixHourWeek(int? positionId = null)
    {
        var punches = new List<Punch>();

        for (int day = 2; day <= 6; day++)
        {
            punches.Add(In(day, 8, positionId));
            punches.Add(Out(day, 17, positionId));
        }

        punches.Add(In(7, 8, positionId));
        punches.Add(Out(7, 9, positionId));

        return punches;
    }

    [Fact]
    public void Cfr778_110a_HourlyRateOnly_FortySixHoursAtTwelveDollars_Pays588()
    {
        // 29 CFR §778.110(a): "If the employee is employed solely on the basis of a single hourly
        // rate, the hourly rate is the 'regular rate.'" The regulation's example: $12 an hour, 46
        // hours worked, total weekly wages $588 (40 × $12 = $480, plus 6 × $18 = $108).
        var result = PayCalculator.Calculate(
            FortySixHourWeek(), Ctx(new Position { Id = 1, BaseRate = 12m }));

        var week = Assert.Single(result.Workweeks);

        Assert.Equal(12m, week.RegularRate);
        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(6m, week.OvertimeHours);
        Assert.Equal(588m, result.GrossPay);
    }

    [Fact]
    public void Cfr778_110b_HourlyRatePlusProductionBonus_RecalculatesRateTo13_Pays637()
    {
        // 29 CFR §778.110(b): the same $12 hourly employee working 46 hours, plus a $46 production
        // bonus. The bonus enters the regular rate, which becomes $13 ($552 + $46 = $598 ÷ 46), and
        // total compensation is $637 (40 × $13 = $520, plus 6 × $19.50 = $117).
        //
        // A production bonus is non-discretionary — it is promised for output, so the employee has a
        // contractual expectation of it. That is precisely why it must enter the regular rate;
        // BonusKind.Discretionary would be excluded (§778.211) and would leave the rate at $12.
        var bonus = TestEntityCreator.CreateTestPunch(
                Instant.FromUtc(2023, 1, 2, 12, 0), PunchKind.FixedDollar, _emp)
            with { Amount = 46m, BonusKind = BonusKind.NonDiscretionary };

        var punches = FortySixHourWeek();
        punches.Add(bonus);

        var result = PayCalculator.Calculate(punches, Ctx(new Position { Id = 1, BaseRate = 12m }));

        var week = Assert.Single(result.Workweeks);

        Assert.Equal(13m, week.RegularRate);
        Assert.Equal(6m, week.OvertimeHours);
        Assert.Equal(637m, result.GrossPay);

        // The overtime premium must be priced off the RECALCULATED rate, not the $12 base — pricing
        // it at $12 would underpay by $6, and is the classic way this regulation gets violated.
        //
        // The six overtime hours arrive as two lines, not one: cumulative hours cross 40 partway
        // through Friday's shift, so five of them attach there and the sixth to Saturday. That is
        // PaySummarizer's "hours accrue toward overtime in the order worked" attribution, and it
        // affects only which shift each line hangs off — never the total, which is what §778.110(b)
        // actually constrains.
        var premiumLines = week.LineItems
            .Where(l => l.Type == PayLineType.OvertimePremium && l.Code == "OVERTIME")
            .ToList();

        Assert.All(premiumLines, line => Assert.Equal(13m, line.BaseRate));
        Assert.Equal(6m, premiumLines.Sum(l => l.Hours));
        Assert.Equal(39m, premiumLines.Sum(l => l.Amount));
    }

    [Fact]
    public void Cfr778_115_TwoRatesInOneWeek_RegularRateIsTheWeightedAverage()
    {
        // 29 CFR §778.115 states the METHOD but gives no worked example — the figures here are ours
        // (see the class doc comment). Per the regulation, where an employee works at two or more
        // different rates in one workweek, total earnings are divided by total hours worked at all
        // jobs to yield the weighted average.
        //
        // 24 hours at $20 = $480, 24 hours at $16 = $384. Weighted average = $864 ÷ 48 = $18/hr,
        // which is deliberately NOT the midpoint of a naive (20 + 16) / 2 — that only coincides
        // here because the hours happen to be equal, so the assertion below would still catch an
        // unweighted average if the hours were ever changed to differ.
        var punches = new List<Punch>();

        for (int day = 2; day <= 4; day++)
        {
            punches.Add(In(day, 8, positionId: 1));
            punches.Add(Out(day, 16, positionId: 1));
        }

        for (int day = 5; day <= 7; day++)
        {
            punches.Add(In(day, 8, positionId: 2));
            punches.Add(Out(day, 16, positionId: 2));
        }

        var result = PayCalculator.Calculate(
            punches,
            Ctx(new Position { Id = 1, BaseRate = 20m }, new Position { Id = 2, BaseRate = 16m }));

        var week = Assert.Single(result.Workweeks);

        Assert.Equal(18m, week.RegularRate);
        Assert.Equal(40m, week.RegularHours);
        Assert.Equal(8m, week.OvertimeHours);

        // $864 straight time + (8 × 0.5 × $18) = $864 + $72 = $936.
        Assert.Equal(936m, result.GrossPay);
    }
}
