using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class CurrentUserService(PayrollDbContext db)
{
    /// <summary>
    /// Claims are the authority for identity and authorization (they're what the tenant filters and
    /// policies already run on); the <c>AppUser</c> row only supplies profile fields the token
    /// doesn't carry. So a caller with no local row still gets a usable answer rather than a 404 —
    /// see <see cref="MeResponse.IsProvisioned"/>.
    /// </summary>
    public async Task<MeResponse> GetAsync(CallerIdentity caller, CancellationToken ct)
    {
        // IgnoreQueryFilters: looking up your own row by primary key. The tenant filter would compare
        // AppUser.ClientId against the caller's own client id, which is circular here and outright
        // wrong for a SystemAdmin (whose row has a null ClientId by design, so it matches nothing).
        var appUser = await db.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.CognitoSub == caller.CognitoSub, ct);

        return new MeResponse
        {
            CognitoSub = caller.CognitoSub,
            Email = caller.Email,
            ClientId = caller.ClientId,
            Role = caller.Role,
            EmployeeId = appUser?.EmployeeId,
            DisplayName = appUser?.DisplayName,
            IsProvisioned = appUser is not null,
        };
    }
}
