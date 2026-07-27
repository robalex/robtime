using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Services;

/// <summary>No DB dependency (computes from HolidayCalendar.UsFederal, a pure static factory), so
/// Singleton — no PayrollDbContext lifetime to match.</summary>
public class HolidayMetadataService
{
    public IReadOnlyList<LocalDate> GetUsFederalHolidays(int year) =>
        HolidayCalendar.UsFederal(year).Dates.OrderBy(d => d).ToList();
}
