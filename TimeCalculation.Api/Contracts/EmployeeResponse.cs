using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public sealed record EmployeeResponse
{
    public required int Id { get; init; }
    public required int ClientId { get; init; }
    public required string FirstName { get; init; }
    public required string MiddleName { get; init; }
    public required string LastName { get; init; }
    public required string Salutation { get; init; }
    public required string PostNominalLetters { get; init; }
    public required decimal MinimumWage { get; init; }
    public required string HomeTimeZoneId { get; init; }
    public required string State { get; init; }

    public static EmployeeResponse FromEntity(Employee employee) => new()
    {
        Id = employee.Id,
        ClientId = employee.ClientId,
        FirstName = employee.FirstName,
        MiddleName = employee.MiddleName,
        LastName = employee.LastName,
        Salutation = employee.Salutation,
        PostNominalLetters = employee.PostNominalLetters,
        MinimumWage = employee.MinimumWage,
        HomeTimeZoneId = employee.HomeTimeZoneId,
        State = employee.State,
    };
}
