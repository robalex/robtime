using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public record CreatePayrollEmployeeIdentifierRequest
{
    public required int EmployeeId { get; init; }
    public required string ExternalEmployeeId { get; init; }
}

/// <summary>No EmployeeId — which employee this row identifies is fixed at creation; correcting a
/// mis-typed provider id doesn't change who it belongs to. A re-point would be delete-and-recreate.</summary>
public record UpdatePayrollEmployeeIdentifierRequest
{
    public required string ExternalEmployeeId { get; init; }
}

public sealed record PayrollEmployeeIdentifierResponse
{
    public required int Id { get; init; }
    public required int ProfileId { get; init; }
    public required int EmployeeId { get; init; }
    public required string ExternalEmployeeId { get; init; }

    public static PayrollEmployeeIdentifierResponse FromEntity(PayrollEmployeeIdentifier identifier) => new()
    {
        Id = identifier.Id,
        ProfileId = identifier.ProfileId,
        EmployeeId = identifier.EmployeeId,
        ExternalEmployeeId = identifier.ExternalEmployeeId,
    };
}
