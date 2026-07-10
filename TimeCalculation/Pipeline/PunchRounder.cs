using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Pipeline;

/// <summary>
/// Stage 1 — Rounding.
/// Sets Punch.RoundedPunchTime according to the PayRule active at the punch's raw time.
/// Clock punches and FixedHours/FixedDollar punches are all rounded if a rule applies.
/// Downstream stages use Punch.EffectiveTime, which prefers the rounded time.
/// </summary>
public static class PunchRounder
{
    public static IReadOnlyList<Punch> Execute(IReadOnlyList<Punch> punches, PipelineContext ctx)
        => punches.Select(p => Round(p, ctx)).ToList();

    private static Punch Round(Punch punch, PipelineContext ctx)
    {
        var rule = ctx.GetRuleAt(punch.PunchTime);
        var rounding = rule.RoundingRule;
        if (rounding.RoundingStrategy == RoundingStrategy.None)
            return punch;

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(punch.PunchTimeZoneId) ?? ctx.EmployeeTimeZone;
        var zonedPunch = punch.PunchTime.InZone(zone);
        var localTime = zonedPunch.TimeOfDay;
        var roundedLocal = ApplyRounding(localTime, rounding);

        if (roundedLocal == localTime)
            return punch;

        // Compute rounded Instant by adding the local-second delta to the original Instant.
        // Avoids InZoneLeniently, which fails at fall-back boundaries: an ambiguous local time
        // (e.g., "1:30 AM Nov 5") always resolves to the pre-transition offset, shifting the
        // result by -1 hour for punches in the second occurrence of that hour.
        var originalSeconds = localTime.Hour * NodaConstants.SecondsPerHour + localTime.Minute * NodaConstants.SecondsPerMinute + localTime.Second;
        var roundedSeconds = roundedLocal.Hour * NodaConstants.SecondsPerHour + roundedLocal.Minute * NodaConstants.SecondsPerMinute;
        var rounded = punch.PunchTime + Duration.FromSeconds(roundedSeconds - originalSeconds);

        return punch with { RoundedPunchTime = rounded };
    }

    private static LocalTime ApplyRounding(LocalTime time, RoundingRule rounding) => rounding.RoundingStrategy switch
    {
        RoundingStrategy.NearestInterval => RoundToNearest(time, rounding.RoundingIntervalMinutes),
        RoundingStrategy.QuarterHourWithGrace => RoundWithGrace(time, rounding.RoundingIntervalMinutes, rounding.RoundingGraceMinutes),
        _ => time
    };

    private static LocalTime RoundToNearest(LocalTime time, int intervalMinutes)
    {
        var totalSeconds = time.Hour * NodaConstants.SecondsPerHour + time.Minute * NodaConstants.SecondsPerMinute + time.Second;
        var intervalSeconds = intervalMinutes * NodaConstants.SecondsPerMinute;
        var rounded = (int)Math.Round((double)totalSeconds / intervalSeconds) * intervalSeconds;
        rounded = Math.Min(rounded, NodaConstants.SecondsPerDay - 1);
        return new LocalTime(rounded / NodaConstants.SecondsPerHour, rounded % NodaConstants.SecondsPerHour / NodaConstants.SecondsPerMinute);
    }

    // Rounds only when the time falls within the grace window on either side of a boundary.
    // Outside the grace window the original time is preserved.
    private static LocalTime RoundWithGrace(LocalTime time, int intervalMinutes, int graceMinutes)
    {
        var totalSeconds = time.Hour * NodaConstants.SecondsPerHour + time.Minute * NodaConstants.SecondsPerMinute + time.Second;
        var intervalSeconds = intervalMinutes * NodaConstants.SecondsPerMinute;
        var graceSeconds = graceMinutes * NodaConstants.SecondsPerMinute;
        var intervalStart = (totalSeconds / intervalSeconds) * intervalSeconds;
        var intervalEnd = intervalStart + intervalSeconds;

        var pastStart = totalSeconds - intervalStart;
        var beforeEnd = intervalEnd - totalSeconds;

        int rounded;
        if (pastStart <= graceSeconds)
            rounded = intervalStart;
        else if (beforeEnd <= graceSeconds)
            rounded = intervalEnd;
        else
            return time;   // outside both grace windows — no rounding

        rounded = Math.Min(rounded, NodaConstants.SecondsPerDay - 1);
        return new LocalTime(rounded / NodaConstants.SecondsPerHour, rounded % NodaConstants.SecondsPerHour / NodaConstants.SecondsPerMinute);
    }
}
