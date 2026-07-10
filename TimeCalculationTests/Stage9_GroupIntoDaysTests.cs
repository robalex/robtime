using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Pipeline;
using Xunit;

namespace TimeCalculationTests;

public class Stage9_GroupIntoDaysTests
{
    private readonly Employee _emp = new() { Id = 1, HomeTimeZoneId = "UTC", MinimumWage = 15m };

    private Shift ShiftOn(LocalDate date, int startHour, int endHour)
    {
        var inP  = TestEntityCreator.CreateTestPunch(
            date.AtMidnight().InZoneLeniently(DateTimeZone.Utc).ToInstant() + Duration.FromHours(startHour), PunchKind.In, _emp);
        var outP = TestEntityCreator.CreateTestPunch(
            date.AtMidnight().InZoneLeniently(DateTimeZone.Utc).ToInstant() + Duration.FromHours(endHour), PunchKind.Out, _emp);
        return new Shift { ShiftDate = date, PunchPairs = [TestEntityCreator.CreateTestPunchPair(inP, outP)] };
    }

    [Fact]
    public void ShiftsOnSameDate_GroupedIntoOneWorkDay()
    {
        var d = new LocalDate(2023, 1, 2);
        var days = Stage9_GroupIntoDays.Execute(
            [ShiftOn(d, 9, 12), ShiftOn(d, 13, 17)], TestEntityCreator.CreateContext(employee: _emp));

        Assert.Single(days);
        Assert.Equal(d, days[0].Date);
        Assert.Equal(2, days[0].Shifts.Count);
        Assert.Equal(7m, days[0].TotalHours);
    }

    [Fact]
    public void ShiftsOnDifferentDates_ProduceSeparateWorkDays_SortedAscending()
    {
        var d1 = new LocalDate(2023, 1, 3);
        var d2 = new LocalDate(2023, 1, 2);
        var days = Stage9_GroupIntoDays.Execute(
            [ShiftOn(d1, 9, 17), ShiftOn(d2, 9, 17)], TestEntityCreator.CreateContext(employee: _emp));

        Assert.Equal(2, days.Count);
        Assert.Equal(d2, days[0].Date);   // sorted ascending
        Assert.Equal(d1, days[1].Date);
    }

    [Fact]
    public void NoShifts_ProducesNoWorkDays()
    {
        var days = Stage9_GroupIntoDays.Execute([], TestEntityCreator.CreateContext(employee: _emp));
        Assert.Empty(days);
    }
}
