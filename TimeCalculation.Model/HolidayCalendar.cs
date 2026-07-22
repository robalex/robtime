using NodaTime;

namespace TimeCalculation.Model;

/// <summary>
/// A set of observed holiday dates.  Federal holidays are generated with the standard observed-day
/// shift (a Saturday holiday is observed the preceding Friday; a Sunday holiday the following Monday).
/// Per-state or per-client calendars can be layered by passing additional dates.
/// </summary>
public class HolidayCalendar
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public IReadOnlySet<LocalDate> Dates { get; set; } = new HashSet<LocalDate>();

    public HolidayCalendar() { }

    public HolidayCalendar(IEnumerable<LocalDate> observedDates) => Dates = observedDates.ToHashSet();

    public bool IsHoliday(LocalDate date) => Dates.Contains(date);

    /// <summary>Applies the federal observed-day rule to an actual holiday date.</summary>
    public static LocalDate Observed(LocalDate actual) => actual.DayOfWeek switch
    {
        IsoDayOfWeek.Saturday => actual.PlusDays(-1),
        IsoDayOfWeek.Sunday => actual.PlusDays(1),
        _ => actual,
    };

    /// <summary>US federal holidays for a year, each shifted to its observed date.</summary>
    public static HolidayCalendar UsFederal(int year)
    {
        var dates = new List<LocalDate>
        {
            Observed(new LocalDate(year, 1, 1)),                  // New Year's Day
            NthWeekday(year, 1, IsoDayOfWeek.Monday, 3),          // MLK Day (3rd Mon Jan)
            NthWeekday(year, 2, IsoDayOfWeek.Monday, 3),          // Washington's Birthday (3rd Mon Feb)
            LastWeekday(year, 5, IsoDayOfWeek.Monday),            // Memorial Day (last Mon May)
            Observed(new LocalDate(year, 6, 19)),                 // Juneteenth
            Observed(new LocalDate(year, 7, 4)),                  // Independence Day
            NthWeekday(year, 9, IsoDayOfWeek.Monday, 1),          // Labor Day (1st Mon Sep)
            NthWeekday(year, 10, IsoDayOfWeek.Monday, 2),         // Columbus Day (2nd Mon Oct)
            Observed(new LocalDate(year, 11, 11)),                // Veterans Day
            NthWeekday(year, 11, IsoDayOfWeek.Thursday, 4),       // Thanksgiving (4th Thu Nov)
            Observed(new LocalDate(year, 12, 25)),                // Christmas
        };
        return new HolidayCalendar(dates);
    }

    private static LocalDate NthWeekday(int year, int month, IsoDayOfWeek day, int n)
    {
        var first = new LocalDate(year, month, 1);
        int offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
        return first.PlusDays(offset + 7 * (n - 1));
    }

    private static LocalDate LastWeekday(int year, int month, IsoDayOfWeek day)
    {
        var last = new LocalDate(year, month, 1).With(DateAdjusters.EndOfMonth);
        int offset = ((int)last.DayOfWeek - (int)day + 7) % 7;
        return last.PlusDays(-offset);
    }
}
