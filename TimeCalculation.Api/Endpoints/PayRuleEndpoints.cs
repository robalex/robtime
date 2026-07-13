using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model.PayRules;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Endpoints;

public static class PayRuleEndpoints
{
    public static void MapPayRuleEndpoints(this WebApplication app)
    {
        app.MapPost("/payrules", CreatePayRule).WithName("CreatePayRule");
    }

    private static async Task<Results<Created<PayRule>, NotFound<string>>> CreatePayRule(
        CreatePayRuleRequest request, PayrollDbContext db, CancellationToken ct)
    {
        var clientExists = await db.Clients.AnyAsync(c => c.Id == request.ClientId, ct);
        if (!clientExists) return TypedResults.NotFound($"No client with id {request.ClientId}.");

        // Only fields the request actually supplied override PayRule's own defaults — never
        // duplicate those defaults here (see CreatePayRuleRequest's doc comment).
        var payRule = new PayRule { ClientId = request.ClientId };
        if (request.Version is { } version) payRule.Version = version;
        if (request.PunchPairResetHours is { } resetHrs) payRule.PunchPairResetHours = resetHrs;
        if (request.MaxShiftLengthHours is { } maxShift) payRule.MaxShiftLengthHours = maxShift;
        if (request.DistanceBetweenShiftsHours is { } distance) payRule.DistanceBetweenShiftsHours = distance;
        if (request.ExpectedBreakLengthMinutes is { } breakLen) payRule.ExpectedBreakLengthMinutes = breakLen;
        if (request.ExpectedLunchLengthMinutes is { } lunchLen) payRule.ExpectedLunchLengthMinutes = lunchLen;
        if (request.ShiftDateStrategy is { } dateStrategy) payRule.ShiftDateStrategy = dateStrategy;
        if (request.WorkweekStartDay is { } weekStart) payRule.WorkweekStartDay = weekStart;
        if (request.ActivePremiumCodes is { } premiumCodes) payRule.ActivePremiumCodes = premiumCodes;

        if (request.RoundingStrategy is { } roundingStrategy) payRule.RoundingRule.RoundingStrategy = roundingStrategy;
        if (request.RoundingIntervalMinutes is { } roundingInterval) payRule.RoundingRule.RoundingIntervalMinutes = roundingInterval;
        if (request.RoundingGraceMinutes is { } roundingGrace) payRule.RoundingRule.RoundingGraceMinutes = roundingGrace;

        if (request.WeeklyOvertimeThresholdHours is { } weeklyOt) payRule.OvertimeRule.WeeklyOvertimeThresholdHours = weeklyOt;
        if (request.HasDailyOvertime is { } hasDaily) payRule.OvertimeRule.HasDailyOvertime = hasDaily;
        if (request.DailyOvertimeThresholdHours is { } dailyOt) payRule.OvertimeRule.DailyOvertimeThresholdHours = dailyOt;
        if (request.DailyDoubletimeThresholdHours is { } dailyDt) payRule.OvertimeRule.DailyDoubletimeThresholdHours = dailyDt;
        if (request.HasSeventhDayRule is { } hasSeventh) payRule.OvertimeRule.HasSeventhDayRule = hasSeventh;

        db.PayRules.Add(payRule);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/payrules/{payRule.Id}", payRule);
    }
}
