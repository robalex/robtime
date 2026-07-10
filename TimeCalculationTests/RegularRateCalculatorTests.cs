using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

public class RegularRateCalculatorTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private PunchPair Pair(int startHour, int endHour, decimal rate)
    {
        var inP  = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, startHour, 0), PunchKind.In,  _emp);
        var outP = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, endHour, 0),  PunchKind.Out, _emp);
        return new PunchPair { InPunch = inP, OutPunch = outP, Rate = rate };
    }

    private static Workweek WeekOf(params Shift[] shifts) => new()
    {
        StartDate = new LocalDate(2023, 1, 1),
        Days = [new WorkDay { Date = new LocalDate(2023, 1, 2), Shifts = shifts }],
    };

    [Fact]
    public void SingleRate_RegularRateEqualsThatRate()
    {
        var week = WeekOf(new Shift { PunchPairs = [Pair(9, 17, 20m)] });   // 8 hrs @ $20
        var result = RegularRateCalculator.Calculate(week);

        Assert.Equal(160m, result.StraightTimeEarnings);
        Assert.Equal(8m, result.TotalHours);
        Assert.Equal(20m, result.RegularRate);
    }

    [Fact]
    public void MultiRate_ProducesWeightedAverage()
    {
        // 8 hrs @ $20 = 160, 2 hrs @ $30 = 60 → (160+60)/10 = 22
        var week = WeekOf(new Shift { PunchPairs = [Pair(9, 17, 20m), Pair(18, 20, 30m)] });
        var result = RegularRateCalculator.Calculate(week);

        Assert.Equal(10m, result.TotalHours);
        Assert.Equal(22m, result.RegularRate);
    }

    [Fact]
    public void NonDiscretionaryBonus_RaisesRegularRate()
    {
        // 10 hrs @ $20 = 200, + $100 bonus → 300/10 = 30
        var bonus = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 12, 0), PunchKind.FixedDollar, _emp)
            with { Amount = 100m, BonusKind = BonusKind.NonDiscretionary };
        var shift = new Shift { PunchPairs = [Pair(9, 19, 20m)], FixedEntries = [bonus] };
        var result = RegularRateCalculator.Calculate(WeekOf(shift));

        Assert.Equal(100m, result.NonDiscretionaryBonuses);
        Assert.Equal(30m, result.RegularRate);
    }

    [Fact]
    public void DiscretionaryBonus_ExcludedFromRegularRate()
    {
        var bonus = TestEntityCreator.CreateTestPunch(Instant.FromUtc(2023, 1, 2, 12, 0), PunchKind.FixedDollar, _emp)
            with { Amount = 100m, BonusKind = BonusKind.Discretionary };
        var shift = new Shift { PunchPairs = [Pair(9, 19, 20m)], FixedEntries = [bonus] };
        var result = RegularRateCalculator.Calculate(WeekOf(shift));

        Assert.Equal(0m, result.NonDiscretionaryBonuses);
        Assert.Equal(20m, result.RegularRate);   // unchanged
    }

    [Fact]
    public void Differentials_IncludedInRegularRate()
    {
        // 10 hrs @ $20 = 200, + $50 differential → 250/10 = 25
        var shift = new Shift
        {
            PunchPairs = [Pair(9, 19, 20m)],
            Differentials = [new AppliedDifferential { Code = "NIGHT", Hours = 5m, Amount = 50m }],
        };
        var result = RegularRateCalculator.Calculate(WeekOf(shift));

        Assert.Equal(50m, result.Differentials);
        Assert.Equal(25m, result.RegularRate);
    }

    [Fact]
    public void NoHours_RegularRateIsZero()
    {
        var result = RegularRateCalculator.Calculate(WeekOf());
        Assert.Equal(0m, result.RegularRate);
        Assert.Equal(0m, result.TotalHours);
    }
}
