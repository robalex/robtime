namespace TimeCalculation.Api.Contracts;

public record CreateEmployeeRequest
{
    public required int ClientId { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public string? Salutation { get; init; }
    public string? PostNominalLetters { get; init; }
    public required decimal MinimumWage { get; init; }
    public string? HomeTimeZoneId { get; init; }
    public string? State { get; init; }
}
