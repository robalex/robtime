using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>Pure request-shape validation — no DB access, so this is unit-testable on its own,
/// matching PunchRequestValidator.</summary>
public static class PunchChangeRequestValidator
{
    public static IDictionary<string, string[]> Validate(SubmitPunchChangeRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors["reason"] = ["Reason is required."];
        }

        switch (request.ChangeKind)
        {
            case PunchChangeKind.Add:
                ValidateAdd(request, errors);
                break;
            case PunchChangeKind.Edit:
                ValidateEdit(request, errors);
                break;
            case PunchChangeKind.Delete:
                ValidateDelete(request, errors);
                break;
        }

        return errors;
    }

    private static void ValidateAdd(SubmitPunchChangeRequestRequest request, Dictionary<string, string[]> errors)
    {
        if (request.PunchId is not null)
        {
            errors["punchId"] = ["PunchId must not be set for an Add request — there's no existing punch yet."];
        }

        if (request.EmployeeId is null)
        {
            errors["employeeId"] = ["EmployeeId is required for an Add request."];
        }

        if (request.PunchTime is null)
        {
            errors["punchTime"] = ["PunchTime is required for an Add request."];
        }

        if (request.Kind is null)
        {
            errors["kind"] = ["Kind is required for an Add request."];
        }

        // Checked here, not deferred to decision time, unlike Edit below — an Add request has no
        // existing punch to merge against and fall back on, so this is knowably wrong the moment
        // it's submitted (same rule PunchRequestValidator.Validate enforces for a direct create).
        if (request.Kind == PunchKind.FixedDollar && request.Amount is null)
        {
            errors["amount"] = ["Amount is required for FixedDollar punches."];
        }

        if (request.Kind == PunchKind.FixedHours && request.Hours is null)
        {
            errors["hours"] = ["Hours is required for FixedHours punches."];
        }
    }

    private static void ValidateEdit(SubmitPunchChangeRequestRequest request, Dictionary<string, string[]> errors)
    {
        if (request.PunchId is null)
        {
            errors["punchId"] = ["PunchId is required for an Edit request."];
        }

        // FixedDollar/FixedHours consistency isn't checked here — an Edit can propose only Kind
        // while relying on the target punch's own existing Amount/Hours, so that combination can
        // only be judged once merged against the real punch. Deferred to decision time, same as
        // PunchRequestValidator.ValidateConsistency does for a direct PUT.
        if (!HasAnyRequestedField(request))
        {
            errors["*"] = ["At least one field must be proposed to change for an Edit request."];
        }
    }

    private static void ValidateDelete(SubmitPunchChangeRequestRequest request, Dictionary<string, string[]> errors)
    {
        if (request.PunchId is null)
        {
            errors["punchId"] = ["PunchId is required for a Delete request."];
        }
    }

    private static bool HasAnyRequestedField(SubmitPunchChangeRequestRequest request) =>
        request.PunchTime is not null
        || request.PunchTimeZoneId is not null
        || request.Kind is not null
        || request.Subtype is not null
        || request.PositionId is not null
        || request.Amount is not null
        || request.Hours is not null
        || request.BonusKind is not null
        || request.CountsTowardRegularRate is not null;
}
