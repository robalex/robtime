using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

public class WeightedOvertimeCalculatorTests
{
    private readonly WeightedOvertimeCalculator _calculator = new();
    private readonly Employee _testEmployee = new() { MinimumWage = 15.00m };

    private Week CreateTestWeek(decimal totalHours, decimal bonus = 0, Employee? employee = null)
    {
        var emp = employee ?? _testEmployee;
        var shifts = new List<Shift>();
        var weekStart = Instant.FromUtc(2023, 1, 2, 9, 0);   // Monday
        var remaining = totalHours;
        var day = 0;

        while (remaining > 0)
        {
            var hoursToday = Math.Min(remaining, 10m);
            var dayStart = weekStart + Duration.FromDays(day);
            var dayEnd = dayStart + Duration.FromHours((double)hoursToday);

            var inPunch  = TestEntityCreator.CreateTestPunch(dayStart, PunchKind.In, emp);
            var outPunch = TestEntityCreator.CreateTestPunch(dayEnd,   PunchKind.Out, emp);
            shifts.Add(new Shift { PunchPairs = [TestEntityCreator.CreateTestPunchPair(inPunch, outPunch)] });

            remaining -= hoursToday;
            day++;
        }

        return new Week(shifts) { NonDiscretionaryBonus = bonus };
    }

    [Fact]
    public void CalculateOvertime_WhenNoHours_ReturnsZero()
    {
        var result = _calculator.CalculateOvertime(new Week(), _testEmployee);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateOvertime_WhenExactly40Hours_ReturnsZero()
    {
        // 4 days × 10 hrs = 40 hours — no OT
        var week = CreateTestWeek(40m);
        Assert.Equal(0m, _calculator.CalculateOvertime(week, _testEmployee));
    }

    [Fact]
    public void CalculateOvertime_When45Hours_ReturnsHalfTimePremium()
    {
        // 45 hours, 5 OT hours; premium = 0.5 × $15 × 5 = $37.50
        var week = CreateTestWeek(45m);
        Assert.Equal(37.50m, _calculator.CalculateOvertime(week, _testEmployee));
    }

    [Fact]
    public void CalculateOvertime_When42Hours_ReturnsHalfTimePremium()
    {
        // 42 hours, 2 OT hours; premium = 0.5 × $15 × 2 = $15.00
        var week = CreateTestWeek(42m);
        Assert.Equal(15.00m, _calculator.CalculateOvertime(week, _testEmployee));
    }

    [Fact]
    public void CalculateOvertime_WhenHigherMinimumWage_ReturnsCorrectPremium()
    {
        // 45 hours at $20/hr; premium = 0.5 × $20 × 5 = $50.00
        var highWage = new Employee { MinimumWage = 20.00m };
        var week = CreateTestWeek(45m, employee: highWage);
        Assert.Equal(50.00m, _calculator.CalculateOvertime(week, highWage));
    }

    [Fact]
    public void CalculateOvertime_WhenBonusApplied_IncreasesOvertimePremium()
    {
        // 45 hrs, $225 bonus; weighted rate = (45×$15 + $225) / 45 = $20; premium = 0.5 × $20 × 5 = $50
        var week = CreateTestWeek(45m, bonus: 225m);
        Assert.Equal(50.00m, _calculator.CalculateOvertime(week, _testEmployee));
    }

    [Fact]
    public void CalculateOvertime_WhenBonusButNoOvertime_ReturnsZero()
    {
        var week = CreateTestWeek(40m, bonus: 200m);
        Assert.Equal(0m, _calculator.CalculateOvertime(week, _testEmployee));
    }
}
