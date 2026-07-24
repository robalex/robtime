using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Auth;

/// <summary>
/// Real Cognito implementation. Not live-testable in this environment — there's no AWS User Pool
/// yet, Terraform for it is blocked on AWS credentials (DEPLOY_PLAN.md §4, UI_PLAN.md Phase 1's
/// sequencing note) — so this is code-complete but unverified against a real pool. Tests use
/// FakeCognitoUserProvisioner instead.
/// </summary>
public sealed class CognitoUserProvisioner(IAmazonCognitoIdentityProvider cognito, IConfiguration configuration) : ICognitoUserProvisioner
{
    public async Task<string> CreateUserAsync(string email, int? clientId, AppRole role, CancellationToken ct)
    {
        var attributes = new List<AttributeType>
        {
            new() { Name = "email", Value = email },
            new() { Name = "email_verified", Value = "true" },
            new() { Name = "custom:role", Value = role.ToString() },
        };
        if (clientId is not null)
        {
            attributes.Add(new AttributeType { Name = "custom:client_id", Value = clientId.Value.ToString() });
        }

        var response = await cognito.AdminCreateUserAsync(new AdminCreateUserRequest
        {
            UserPoolId = UserPoolId,
            Username = email,
            UserAttributes = attributes,
            DesiredDeliveryMediums = ["EMAIL"],
        }, ct);

        return response.User.Attributes.FirstOrDefault(a => a.Name == "sub")?.Value
            ?? throw new InvalidOperationException("Cognito AdminCreateUser response had no 'sub' attribute.");
    }

    public Task DeleteUserAsync(string username, CancellationToken ct) =>
        cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest { UserPoolId = UserPoolId, Username = username }, ct);

    private string UserPoolId => configuration["Cognito:UserPoolId"]
        ?? throw new InvalidOperationException("Cognito:UserPoolId is not configured.");
}
