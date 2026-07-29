using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

public sealed record TimecardLineItemResponse
{
    public required PayLineType Type { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Amount { get; init; }
    public decimal? BaseRate { get; init; }
    public decimal? Multiplier { get; init; }

    public static TimecardLineItemResponse FromDomain(PayLineItem item, bool showFullItemization) => new()
    {
        Type = item.Type,
        Code = item.Code,
        Description = item.Description,
        Hours = item.Hours,
        Amount = item.Amount,
        // BaseRate/Multiplier on an OvertimePremium line literally ARE the week's weighted FLSA
        // regular rate (PayLineItem's own doc comment: "BaseRate = the week's regular rate") — the
        // one piece UI_PLAN.md §9 decision 19 draws the line at hiding. Every other line type's
        // BaseRate is the employee's own straight rate or a flat differential rate, which the
        // decision explicitly says is fine to show even with itemization off.
        BaseRate = showFullItemization || item.Type != PayLineType.OvertimePremium ? item.BaseRate : null,
        Multiplier = showFullItemization || item.Type != PayLineType.OvertimePremium ? item.Multiplier : null,
    };
}

/// <summary>One side of a punch pair. Time is the raw clock time and RoundedTime the post-Stage-1
/// value; both are carried because the timecard shows them side by side when they differ (UI_PLAN.md
/// §8), which a single "effective" time would make impossible to display.</summary>
public sealed record TimecardPunchResponse
{
    public required int Id { get; init; }
    public required Instant Time { get; init; }
    public Instant? RoundedTime { get; init; }

    /// <summary>Break/Lunch classification of the gap adjoining this punch. PunchSubtypeInferrer
    /// writes a gap's subtype onto both punches that bound it, so a pair's Out carries the subtype of
    /// the gap that *follows* it and its In the gap that *precedes* it.</summary>
    public PunchSubtype? Subtype { get; init; }

    public static TimecardPunchResponse FromDomain(Punch punch) => new()
    {
        Id = punch.Id,
        Time = punch.PunchTime,
        RoundedTime = punch.RoundedPunchTime,
        Subtype = punch.Subtype,
    };
}

/// <summary>
/// One In→Out pair within a shift. In or Out is null for an incomplete pair — a clock-in with no
/// clock-out, or the reverse — which the pipeline surfaces deliberately rather than discarding, and
/// which the timecard's exceptions section is built to show.
/// </summary>
public sealed record TimecardPunchPairResponse
{
    public TimecardPunchResponse? In { get; init; }
    public TimecardPunchResponse? Out { get; init; }
    public int? PositionId { get; init; }
    public string? PositionName { get; init; }

    /// <summary>The pair's own straight-time rate. Not gated by showFullItemization: decision 19
    /// withholds the *weighted regular rate* derivation from employees, and explicitly still shows
    /// them their own rate.</summary>
    public decimal? Rate { get; init; }

    public required decimal Hours { get; init; }
    public required bool IsIncomplete { get; init; }

    /// <summary>True when this pair came from splitting one clock-in/clock-out span at a PayRule or
    /// position effective-date boundary — so the UI can explain why one visit to the clock shows as
    /// two rows rather than looking like duplicate data.</summary>
    public required bool IsSplit { get; init; }

    public static TimecardPunchPairResponse FromDomain(PunchPair pair) => new()
    {
        In = pair.InPunch is not null ? TimecardPunchResponse.FromDomain(pair.InPunch) : null,
        Out = pair.OutPunch is not null ? TimecardPunchResponse.FromDomain(pair.OutPunch) : null,
        PositionId = pair.Position?.Id,
        PositionName = pair.Position?.Name,
        Rate = pair.Rate,
        Hours = pair.TotalHours,
        IsIncomplete = pair.IsMissingPunch,
        IsSplit = pair.IsSplit,
    };
}

/// <summary>A FixedDollar or FixedHours punch (a bonus, or flat paid hours such as leave). Carried
/// on the shift alongside the clock pairs so every Punch row in the period is visible somewhere on
/// the timecard — otherwise a bonus would appear as a pay line with no entry explaining it.</summary>
public sealed record TimecardFixedEntryResponse
{
    public required int Id { get; init; }
    public required Instant Time { get; init; }
    public required PunchKind Kind { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Hours { get; init; }

    public static TimecardFixedEntryResponse FromDomain(Punch punch) => new()
    {
        Id = punch.Id,
        Time = punch.PunchTime,
        Kind = punch.Kind,
        Amount = punch.Amount,
        Hours = punch.Hours,
    };
}

public sealed record TimecardShiftResponse
{
    public required LocalDate ShiftDate { get; init; }
    public required int AnchorPunchId { get; init; }
    public required List<TimecardPunchPairResponse> Pairs { get; init; }
    public required List<TimecardFixedEntryResponse> FixedEntries { get; init; }
    public required List<TimecardLineItemResponse> LineItems { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal Gross { get; init; }

    /// <summary>pay is null when the grouping produced a shift the summarizer emitted nothing for —
    /// an all-incomplete shift with no payable hours. Rendering it with zero pay is right; dropping
    /// it would hide the very exception the employee needs to fix.</summary>
    public static TimecardShiftResponse FromDomain(Shift shift, ShiftPay? pay, bool showFullItemization) => new()
    {
        ShiftDate = shift.ShiftDate,
        AnchorPunchId = shift.AnchorPunchId,
        Pairs = shift.PunchPairs.Select(TimecardPunchPairResponse.FromDomain).ToList(),
        FixedEntries = shift.FixedEntries.Select(TimecardFixedEntryResponse.FromDomain).ToList(),
        LineItems = pay is null
            ? []
            : pay.LineItems.Select(l => TimecardLineItemResponse.FromDomain(l, showFullItemization)).ToList(),
        TotalHours = shift.TotalHours,
        Gross = pay?.Gross ?? 0m,
    };
}

/// <summary>
/// One calendar day. Emitted for every day of the period, including days with no punches at all
/// (Shifts empty) — the pay breakdown is grouped by FLSA workweek, and without the quiet empty rows
/// it is genuinely hard to see where one week ends and the next begins, which is exactly the
/// ambiguity that makes overtime totals look wrong.
/// </summary>
public sealed record TimecardDayResponse
{
    public required LocalDate Date { get; init; }
    public required List<TimecardShiftResponse> Shifts { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal Gross { get; init; }
}

public sealed record TimecardWorkweekResponse
{
    public required LocalDate WeekStart { get; init; }

    /// <summary>Null when itemization is limited (UI_PLAN.md §9 decision 19) — this field IS the
    /// weighted FLSA regular-rate derivation itself, not just a line-item detail.</summary>
    public decimal? RegularRate { get; init; }

    public required decimal RegularHours { get; init; }
    public required decimal OvertimeHours { get; init; }
    public required decimal DoubletimeHours { get; init; }
    public required List<TimecardDayResponse> Days { get; init; }
    public required decimal Gross { get; init; }

    /// <summary>
    /// Zips the grouping graph (week → day → shift → pair) against the pay graph
    /// (WorkweekPay → ShiftPay → PayLineItem) on the (ShiftDate, AnchorPunchId) identity
    /// Shift.AnchorPunchId exists to provide — see PayCalculationDetail for why both are needed.
    ///
    /// periodStart/periodEnd clamp which empty days get emitted: a week at either end of the pay
    /// period is usually partial, and days outside the period belong to a neighbouring timecard.
    /// </summary>
    public static TimecardWorkweekResponse FromDomain(
        Workweek week, WorkweekPay pay, LocalDate periodStart, LocalDate periodEnd, bool showFullItemization)
    {
        var payByShift = pay.Shifts.ToDictionary(s => new ShiftKey(s.ShiftDate, s.AnchorPunchId));
        var shiftsByDate = week.Days
            .SelectMany(d => d.Shifts)
            .GroupBy(s => s.ShiftDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = DatesToRender(week.StartDate, shiftsByDate.Keys, periodStart, periodEnd)
            .Select(date => BuildDay(date, shiftsByDate, payByShift, showFullItemization))
            .ToList();

        return new TimecardWorkweekResponse
        {
            WeekStart = week.StartDate,
            RegularRate = showFullItemization ? pay.RegularRate : null,
            RegularHours = pay.RegularHours,
            OvertimeHours = pay.OvertimeHours,
            DoubletimeHours = pay.DoubletimeHours,
            Days = days,
            Gross = pay.Gross,
        };
    }

    /// <summary>
    /// A week with no shifts at all. WorkDayGrouper emits no WorkDay for a shiftless date and
    /// WorkweekGrouper groups from WorkDays, so such a week never reaches PayCalculationDetail —
    /// left to TimecardResponse.FromDomain alone, that week would be missing from the timecard
    /// entirely rather than showing as quiet empty days, which is exactly the week-boundary ambiguity
    /// the day-level grouping exists to resolve. RegularRate is null unconditionally (not gated by
    /// showFullItemization) because there is no rate computation to hide or reveal — zero hours means
    /// nothing was derived.
    /// </summary>
    public static TimecardWorkweekResponse Empty(LocalDate weekStart, LocalDate periodStart, LocalDate periodEnd)
    {
        var days = DatesToRender(weekStart, [], periodStart, periodEnd)
            .Select(date => new TimecardDayResponse { Date = date, Shifts = [], TotalHours = 0m, Gross = 0m })
            .ToList();

        return new TimecardWorkweekResponse
        {
            WeekStart = weekStart,
            RegularRate = null,
            RegularHours = 0m,
            OvertimeHours = 0m,
            DoubletimeHours = 0m,
            Days = days,
            Gross = 0m,
        };
    }

    /// <summary>
    /// The week's days clipped to the pay period, unioned with the dates of any shift actually in
    /// the week. The union is deliberate rather than defensive tidiness: ShiftDater can date a
    /// midnight-crossing shift onto a day outside the clipped range, and silently dropping it would
    /// remove paid hours from a screen whose totals still include them.
    /// </summary>
    private static IEnumerable<LocalDate> DatesToRender(
        LocalDate weekStart, IEnumerable<LocalDate> shiftDates, LocalDate periodStart, LocalDate periodEnd)
    {
        var weekEnd = weekStart.PlusDays(6);
        var from = periodStart > weekStart ? periodStart : weekStart;
        var to = periodEnd < weekEnd ? periodEnd : weekEnd;

        var dates = new SortedSet<LocalDate>(shiftDates);
        for (var date = from; date <= to; date = date.PlusDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }

    private static TimecardDayResponse BuildDay(
        LocalDate date,
        IReadOnlyDictionary<LocalDate, List<Shift>> shiftsByDate,
        IReadOnlyDictionary<ShiftKey, ShiftPay> payByShift,
        bool showFullItemization)
    {
        var shifts = shiftsByDate.TryGetValue(date, out var found) ? found : [];
        var responses = shifts
            .Select(s => TimecardShiftResponse.FromDomain(
                s,
                payByShift.TryGetValue(new ShiftKey(s.ShiftDate, s.AnchorPunchId), out var pay) ? pay : null,
                showFullItemization))
            .ToList();

        return new TimecardDayResponse
        {
            Date = date,
            Shifts = responses,
            TotalHours = responses.Sum(s => s.TotalHours),
            Gross = responses.Sum(s => s.Gross),
        };
    }

    /// <summary>The (ShiftDate, AnchorPunchId) identity joining the grouping graph to the pay graph.
    /// A record rather than a tuple, per CLAUDE.md.</summary>
    private sealed record ShiftKey(LocalDate ShiftDate, int AnchorPunchId);
}

/// <summary>
/// Phase 6.3's timecard endpoint response, extended in 6.5 to carry punch-level detail — a
/// role-gated rendering of PayCalculator's output for one employee's actual pay period.
///
/// This shape is the timecard's **view model contract** (UI_PLAN.md decision 25): the live
/// calculation below is its first producer, and Phase 6.7's approved-timecard snapshot is a second
/// producer of this same shape rather than a second rendering of the timecard. Anything the screen
/// needs has to be reachable from here, because after approval that is all it will get.
///
/// PeriodStart/PeriodEnd are both inclusive, matching PayPeriod's own convention — deliberately
/// different from the exclusive-end convention this API otherwise uses for date range query
/// parameters (see PunchEndpoints' from/to), because these are the resolved PayPeriod's own bounds
/// echoed back, not a caller-supplied range.
/// </summary>
public sealed record TimecardResponse
{
    public required int EmployeeId { get; init; }
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }
    public required int PayRuleId { get; init; }
    public required string PayRuleName { get; init; }
    public required int PayRuleVersion { get; init; }
    public required decimal GrossPay { get; init; }
    public required List<TimecardWorkweekResponse> Workweeks { get; init; }

    /// <summary>True when this period has been approved and is rendering from a frozen
    /// PayCalculationSnapshot rather than a live calculation (Phase 6.7, UI_PLAN.md decision 21) —
    /// every punch-mutating path is blocked for this employee/period while this is true.</summary>
    public bool IsLocked { get; init; }
    public string? ApprovedByUserId { get; init; }
    public Instant? ApprovedAt { get; init; }

    /// <summary>
    /// Takes the PayRule's identity fields discretely rather than a live PayRule entity, so this same
    /// method builds the response from either a just-computed PayCalculationDetail (the live path,
    /// TimecardService.GetAsync) or one deserialized from a frozen PayCalculationSnapshot (the locked
    /// path) — the snapshot has no live PayRule to hand back, only these three identity fields plus
    /// the WorkweekStartDay the grouping itself was already built with.
    /// </summary>
    public static TimecardResponse FromDomain(
        PayCalculationDetail detail, int payRuleId, string payRuleName, int payRuleVersion,
        IsoDayOfWeek workweekStartDay, LocalDate periodStart, LocalDate periodEnd, bool showFullItemization)
    {
        // Matched on WeekStart rather than by position: both lists come from the same ordered pass in
        // PayCalculator.CalculateDetailed, so index alignment holds today, but keying on the date the
        // two records already share means it keeps holding if either ever gains a filter.
        var payByWeek = detail.Result.Workweeks.ToDictionary(w => w.WeekStart);
        var weeksByStart = detail.Weeks.ToDictionary(w => w.StartDate);

        // Iterate every calendar week the period touches, not just the ones PayCalculationDetail
        // returned — WorkDayGrouper/WorkweekGrouper only produce a week that has at least one shift
        // in it, so a period where the employee worked one week of a two-week period would otherwise
        // silently render as a one-week timecard. See TimecardWorkweekResponse.Empty.
        var weeks = WeekStartsInPeriod(periodStart, periodEnd, workweekStartDay)
            .Select(weekStart => weeksByStart.TryGetValue(weekStart, out var week) && payByWeek.TryGetValue(weekStart, out var pay)
                ? TimecardWorkweekResponse.FromDomain(week, pay, periodStart, periodEnd, showFullItemization)
                : TimecardWorkweekResponse.Empty(weekStart, periodStart, periodEnd))
            .ToList();

        return new TimecardResponse
        {
            EmployeeId = detail.Result.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayRuleId = payRuleId,
            PayRuleName = payRuleName,
            PayRuleVersion = payRuleVersion,
            GrossPay = detail.Result.GrossPay,
            Workweeks = weeks,
        };
    }

    /// <summary>Every FLSA week-start date the period touches, oldest first. Mirrors
    /// WorkweekGrouper.WeekStartFor exactly (duplicated rather than shared: that method is a private
    /// engine implementation detail, and this is a display concern over its already-published output)
    /// — if that anchor math ever changes, this must change with it.</summary>
    private static IEnumerable<LocalDate> WeekStartsInPeriod(LocalDate periodStart, LocalDate periodEnd, IsoDayOfWeek anchor)
    {
        for (var weekStart = WeekStartFor(periodStart, anchor); weekStart <= periodEnd; weekStart = weekStart.PlusDays(7))
        {
            yield return weekStart;
        }
    }

    /// <summary>Most recent date on or before <paramref name="date"/> whose day-of-week is the anchor.</summary>
    private static LocalDate WeekStartFor(LocalDate date, IsoDayOfWeek anchor)
    {
        int diff = ((int)date.DayOfWeek - (int)anchor + 7) % 7;
        return date.PlusDays(-diff);
    }
}
