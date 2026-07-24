using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public record CreateUserRequest
{
    public required string Email { get; init; }

    /// <summary>Required for every role except SystemAdmin — see AppRole's doc comment.</summary>
    public int? ClientId { get; init; }

    /// <summary>Set when this user IS an employee (self-service access).</summary>
    public int? EmployeeId { get; init; }

    public required string DisplayName { get; init; }
    public required AppRole Role { get; init; }
}
