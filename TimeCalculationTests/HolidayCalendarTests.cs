using NodaTime;
using TimeCalculation.Model;
using Xunit;

namespace TimeCalculationTests;

public class HolidayCalendarTests
{
    [Fact]
    public void UsFederal_2023_IncludesFixedAndFloatingHolidays()
    {
        var cal = HolidayCalendar.UsFederal(2023);

        Assert.True(cal.IsHoliday(new LocalDate(2023, 1, 2)));    // New Year's (Jan 1 Sun → observed Mon Jan 2)
        Assert.True(cal.IsHoliday(new LocalDate(2023, 1, 16)));   // MLK, 3rd Mon Jan
        Assert.True(cal.IsHoliday(new LocalDate(2023, 5, 29)));   // Memorial, last Mon May
        Assert.True(cal.IsHoliday(new LocalDate(2023, 6, 19)));   // Juneteenth (Mon)
        Assert.True(cal.IsHoliday(new LocalDate(2023, 7, 4)));    // Independence Day (Tue)
        Assert.True(cal.IsHoliday(new LocalDate(2023, 9, 4)));    // Labor, 1st Mon Sep
        Assert.True(cal.IsHoliday(new LocalDate(2023, 11, 23)));  // Thanksgiving, 4th Thu Nov
        Assert.True(cal.IsHoliday(new LocalDate(2023, 12, 25)));  // Christmas (Mon)
    }

    [Fact]
    public void Observed_SaturdayHoliday_ShiftsToFriday()
    {
        // July 4 2020 was a Saturday → observed Friday July 3
        Assert.Equal(new LocalDate(2020, 7, 3), HolidayCalendar.Observed(new LocalDate(2020, 7, 4)));
    }

    [Fact]
    public void Observed_SundayHoliday_ShiftsToMonday()
    {
        // Dec 25 2022 was a Sunday → observed Monday Dec 26
        Assert.Equal(new LocalDate(2022, 12, 26), HolidayCalendar.Observed(new LocalDate(2022, 12, 25)));
    }

    [Fact]
    public void Observed_WeekdayHoliday_Unchanged()
    {
        Assert.Equal(new LocalDate(2023, 7, 4), HolidayCalendar.Observed(new LocalDate(2023, 7, 4)));
    }

    [Fact]
    public void NonHolidayDate_ReturnsFalse()
    {
        var cal = HolidayCalendar.UsFederal(2023);
        Assert.False(cal.IsHoliday(new LocalDate(2023, 3, 15)));
    }
}
