using System.Collections.Concurrent;
using TimeCalculation.Api.Auth;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Tests;

/// <summary>Stands in for a real Cognito User Pool in tests — same reasoning as TestAuthHandler:
/// there's no Testcontainers-equivalent to spin one up per test run. Registered in ApiFixture,
/// replacing the real CognitoUserProvisioner for the whole test host.</summary>
public sealed class FakeCognitoUserProvisioner : ICognitoUserProvisioner
{
    private readonly ConcurrentDictionary<string, byte> _usernames = new();

    public Task<string> CreateUserAsync(string email, int? clientId, AppRole role, CancellationToken ct)
    {
        var sub = Guid.NewGuid().ToString();
        _usernames[email] = 0;
        return Task.FromResult(sub);
    }

    public Task DeleteUserAsync(string username, CancellationToken ct)
    {
        _usernames.TryRemove(username, out _);
        return Task.CompletedTask;
    }
}
