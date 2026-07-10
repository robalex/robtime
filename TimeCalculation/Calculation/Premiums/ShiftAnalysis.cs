using TimeCalculation.Model;

namespace TimeCalculation.Calculation.Premiums;

/// <summary>A mid-shift gap between an Out and the following In, with its classified subtype.</summary>
public record ShiftGap(PunchSubtype Subtype, decimal DurationMinutes, decimal WorkedHoursBefore);

/// <summary>
/// Derived view of a shift used by premium rules: total hours worked and the ordered list of
/// break/lunch gaps, each annotated with how many hours were worked before it began (so rules can
/// enforce "a meal by the 5th hour").
///
/// Note: rest breaks are typically paid and may not be clocked; this analysis can only see gaps
/// that were actually punched.  Rest-premium rules therefore treat clocked Break gaps as the
/// evidence of rest provided — employers who don't clock paid rests must assert compliance via
/// an override.
/// </summary>
public record ShiftAnalysis
{
    public decimal WorkedHours { get; init; }
    public IReadOnlyList<ShiftGap> Gaps { get; init; } = [];

    public static ShiftAnalysis From(Shift shift)
    {
        var pairs = shift.PunchPairs
            .Where(p => !p.IsMissingPunch)
            .OrderBy(p => p.InPunch!.EffectiveTime)
            .ToList();

        var gaps = new List<ShiftGap>();
        decimal workedBefore = 0;

        for (int i = 0; i < pairs.Count; i++)
        {
            workedBefore += pairs[i].TotalHours;

            if (i + 1 < pairs.Count)
            {
                var gapStart = pairs[i].OutPunch!;
                var gapEnd = pairs[i + 1].InPunch!;
                var minutes = (decimal)(gapEnd.EffectiveTime - gapStart.EffectiveTime).TotalMinutes;
                var subtype = gapStart.Subtype ?? gapEnd.Subtype ?? PunchSubtype.None;
                gaps.Add(new ShiftGap(subtype, minutes, workedBefore));
            }
        }

        return new ShiftAnalysis { WorkedHours = pairs.Sum(p => p.TotalHours), Gaps = gaps };
    }

    /// <summary>True if a Lunch gap of at least <paramref name="minMinutes"/> began no later than
    /// <paramref name="byWorkedHour"/> hours into the shift.  WorkedHoursBefore is the time worked
    /// up to the start of the gap.</summary>
    public bool HasQualifyingMeal(decimal minMinutes, decimal byWorkedHour) =>
        Gaps.Any(g => g.Subtype == PunchSubtype.Lunch
                      && g.DurationMinutes >= minMinutes
                      && g.WorkedHoursBefore <= byWorkedHour);

    public int QualifyingMealCount(decimal minMinutes) =>
        Gaps.Count(g => g.Subtype == PunchSubtype.Lunch && g.DurationMinutes >= minMinutes);

    public int RestBreakCount() => Gaps.Count(g => g.Subtype == PunchSubtype.Break);
}
