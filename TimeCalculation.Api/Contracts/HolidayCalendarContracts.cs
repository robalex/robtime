using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public record CreateHolidayCalendarRequest
{
    public required int ClientId { get; init; }
    public required string Name { get; init; }
    public HashSet<LocalDate>? Dates { get; init; }
}

public record UpdateHolidayCalendarRequest
{
    public required string Name { get; init; }
    public HashSet<LocalDate>? Dates { get; init; }
}

public sealed record HolidayCalendarResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string Name { get; init; }
    public required HashSet<LocalDate> Dates { get; init; }

    public static HolidayCalendarResponse FromEntity(HolidayCalendar calendar) => new()
    {
        Id = calendar.Id,
        ClientId = calendar.ClientId,
        Name = calendar.Name,
        Dates = calendar.Dates.ToHashSet(),
    };
}
