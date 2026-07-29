using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Builds an updated <see cref="Punch"/> from an existing one plus a request — Punch is an immutable
/// record (unlike PayRule/Employee's mutable classes, deliberately: the calculation engine's pipeline
/// stages rely on Punch never mutating out from under them), so "apply an update" here means
/// producing a new value via <c>with</c>, not mutating <paramref name="existing"/> in place. Pure, no
/// DB access — unit-testable on its own, matching PayRuleRequestMapper.
/// </summary>
public static class PunchRequestMapper
{
    public static Punch ApplyUpdate(Punch existing, UpdatePunchRequest request) => existing with
    {
        PunchTime = request.PunchTime ?? existing.PunchTime,
        PunchTimeZoneId = request.PunchTimeZoneId ?? existing.PunchTimeZoneId,
        Kind = request.Kind ?? existing.Kind,
        Subtype = request.Subtype ?? existing.Subtype,
        PositionId = request.PositionId ?? existing.PositionId,
        Amount = request.Amount ?? existing.Amount,
        Hours = request.Hours ?? existing.Hours,
        BonusKind = request.BonusKind ?? existing.BonusKind,
        CountsTowardRegularRate = request.CountsTowardRegularRate ?? existing.CountsTowardRegularRate,
    };
}
