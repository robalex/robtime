using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Validation;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Services;

public class UserProvisioningService(PayrollDbContext db, ICognitoUserProvisioner cognito)
{
    public async Task<ServiceResult<AppUser>> CreateAsync(
        CreateUserRequest request, AppRole callerRole, int? callerClientId, CancellationToken ct)
    {
        var errors = UserRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<AppUser>.ValidationFailed(errors);
        }

        // ClientAdmin can only provision users into their own client. SystemAdmin can target any
        // client — bootstrapping a brand-new client's first ClientAdmin is the same cross-tenant
        // exception the Client endpoints already carve out (UI_PLAN.md §5's SystemAdmin-scoping
        // decision), so this doesn't get its own separate rule.
        if (callerRole != AppRole.SystemAdmin && request.ClientId != callerClientId)
        {
            return ServiceResult<AppUser>.Forbidden("You can only create users within your own client.");
        }

        // IgnoreQueryFilters: these are plain existence checks, not a read the caller is meant to see
        // filtered results from — the Forbidden guard above already established the caller may act on
        // this ClientId (either it's their own tenant, or they're SystemAdmin). Without this, a
        // SystemAdmin's own _tenantClientId (null, no client selected) would filter every client to
        // nothing and turn "create the first ClientAdmin for a brand-new client" into an impossible
        // 404, defeating the one thing SystemAdmin-scoping exists to allow (UI_PLAN.md §5).
        if (request.ClientId is { } clientId)
        {
            var clientExists = await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id == clientId && !c.IsDeleted, ct);
            if (!clientExists)
            {
                return ServiceResult<AppUser>.NotFound($"No client with id {clientId}.");
            }
        }

        if (request.EmployeeId is { } employeeId)
        {
            var employeeExists = await db.Employees.IgnoreQueryFilters()
                .AnyAsync(e => e.Id == employeeId && e.ClientId == request.ClientId && !e.IsDeleted, ct);
            if (!employeeExists)
            {
                return ServiceResult<AppUser>.NotFound($"No employee with id {employeeId} for client {request.ClientId}.");
            }
        }

        var cognitoSub = await cognito.CreateUserAsync(request.Email, request.ClientId, request.Role, ct);

        var user = new AppUser
        {
            CognitoSub = cognitoSub,
            ClientId = request.ClientId,
            EmployeeId = request.EmployeeId,
            DisplayName = request.DisplayName,
            Role = request.Role,
        };

        try
        {
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Compensate: the Cognito user must not outlive the failed local write, or a retry with
            // the same email hits Cognito's own "user already exists" error with no local row to
            // show for it. Best-effort — no saga/outbox here (UI_PLAN.md §5 flags this as a known
            // limitation of the two-system write) — if the compensating delete also fails, the
            // orphaned Cognito user needs manual cleanup. Swallowed so the original DbUpdateException
            // below is what actually surfaces to the caller, not a secondary failure masking it.
            try
            {
                await cognito.DeleteUserAsync(request.Email, ct);
            }
            catch
            {
                // See comment above — best-effort only.
            }

            throw;
        }

        return ServiceResult<AppUser>.Success(user);
    }
}
