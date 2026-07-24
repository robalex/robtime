using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public record UserResponse
{
    public required string CognitoSub { get; init; }
    public int? ClientId { get; init; }
    public int? EmployeeId { get; init; }
    public required string DisplayName { get; init; }
    public required AppRole Role { get; init; }

    public static UserResponse FromEntity(AppUser user) => new()
    {
        CognitoSub = user.CognitoSub,
        ClientId = user.ClientId,
        EmployeeId = user.EmployeeId,
        DisplayName = user.DisplayName,
        Role = user.Role,
    };
}
