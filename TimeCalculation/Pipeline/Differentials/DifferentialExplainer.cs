using NodaTime;
using TimeCalculation.Calculation;
using TimeCalculation.Model;

namespace TimeCalculation.Pipeline.Differentials;

/// <summary>
/// The differential sandbox's "why did/didn't this apply" engine — runs Stages 1–8b (shift
/// preparation, the real DifferentialApplier, and the real RangeDifferentialQualifier) over a set of
/// (typically hand-entered test) punches, then evaluates *every* client differential against *every*
/// shift, reporting a DifferentialOutcome for each. DifferentialApplier only ever reports winners;
/// this reports winners AND why every loser lost, by composing the same shared primitives
/// (DifferentialDaySchedule, the per-pair Segments() calculators, ExclusivityResolver,
/// RangeOccurrenceHours, DifferentialZoneProjector) rather than a second decision implementation —
/// the whole point is that this can never disagree with what real payroll would actually do.
/// </summary>
public static class DifferentialExplainer
{
    public static IReadOnlyList<ShiftDifferentialExplanation> Explain(IReadOnlyList<Punch> punches, PipelineContext ctx)
    {
        var shifts = PayCalculator.PrepareShifts(punches, ctx);

        // The real Stage 8 result: each shift's post-exclusivity winners. Needed as input to
        // RangeOccurrenceHours below — the exact same list RangeDifferentialQualifier itself reads —
        // so a ConsecutiveDayRange occurrence's summed hours reflect every shift that contributed to
        // it, not just the one shift currently being explained.
        var shiftsWithDifferentials = DifferentialApplier.ApplyDifferentials(shifts, ctx);

        var rangeRules = ctx.DifferentialRules
            .Where(r => r.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange && r.MinHoursInRange > 0)
            .ToList();
        var rangeSumsByCode = rangeRules.ToDictionary(
            r => r.Code, r => RangeOccurrenceHours.SumByOccurrenceAnchor(r, shiftsWithDifferentials));

        return shifts.Select(shift => ExplainShift(shift, rangeSumsByCode, ctx)).ToList();
    }

    private static ShiftDifferentialExplanation ExplainShift(
        Shift shift, IReadOnlyDictionary<string, Dictionary<LocalDate, decimal>> rangeSumsByCode, PipelineContext ctx)
    {
        var evaluations = shift.HasMissingPunches
            ? ctx.DifferentialRules.Select(MissingPunchesEvaluation).ToList()
            : EvaluateAllRules(shift, rangeSumsByCode, ctx);

        return new ShiftDifferentialExplanation
        {
            ShiftDate = shift.ShiftDate,
            AnchorPunchId = shift.AnchorPunchId,
            Evaluations = evaluations,
        };
    }

    private static List<DifferentialEvaluation> EvaluateAllRules(
        Shift shift, IReadOnlyDictionary<string, Dictionary<LocalDate, decimal>> rangeSumsByCode, PipelineContext ctx)
    {
        var activeCodes = ctx.GetRuleAt(FirstInstant(shift)).ActiveDifferentialCodes;
        var results = new List<DifferentialEvaluation>();
        var candidateMeasurements = new Dictionary<string, CandidateMeasurement>();

        foreach (var rule in ctx.DifferentialRules)
        {
            if (!activeCodes.Contains(rule.Code))
            {
                results.Add(NotEnabledEvaluation(rule));
                continue;
            }

            var measurement = Measure(rule, shift, ctx);
            if (measurement.Hours <= 0)
            {
                results.Add(NoOverlapEvaluation(rule, shift, ctx));
                continue;
            }

            if (measurement.Hours < rule.MinHoursInWindow)
            {
                results.Add(BelowWindowThresholdEvaluation(rule, measurement));
                continue;
            }

            candidateMeasurements[rule.Code] = measurement;
        }

        // Exclusivity is judged once, over exactly the rules that individually qualified on this
        // shift — the same resolver DifferentialApplier itself calls.
        var candidates = candidateMeasurements
            .Select(kv => ToCandidate(ctx.DifferentialRules.First(r => r.Code == kv.Key), kv.Value))
            .ToList();
        var exclusivityByCode = ExclusivityResolver.Resolve(candidates)
            .ToDictionary(o => o.Candidate.Rule.Code);

        foreach (var (code, measurement) in candidateMeasurements)
        {
            var rule = ctx.DifferentialRules.First(r => r.Code == code);
            var exclusivity = exclusivityByCode[code];

            if (!exclusivity.Won)
            {
                results.Add(SupersededEvaluation(rule, measurement, exclusivity.SupersededByCode!));
                continue;
            }

            if (rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange && rule.MinHoursInRange > 0)
            {
                var anchor = DayOfWeekRange.OccurrenceAnchor(shift.ShiftDate, rule.DayOfWeekRangeStart);
                var occurrenceHours = rangeSumsByCode[code].GetValueOrDefault(anchor);
                if (occurrenceHours < rule.MinHoursInRange)
                {
                    results.Add(BelowRangeThresholdEvaluation(rule, measurement, anchor, occurrenceHours));
                    continue;
                }
            }

            results.Add(AppliedEvaluation(rule, measurement));
        }

        return results;
    }

    // Mirrors DifferentialApplier's own per-pair loop exactly (same Segments()/Calculate() calls,
    // same amount formula) so Measure's Hours/Amount can never diverge from what a real
    // AppliedDifferential would carry.
    private static CandidateMeasurement Measure(DifferentialRule rule, Shift shift, PipelineContext ctx)
    {
        decimal qualifyingHours = 0;
        decimal perHourAmount = 0;
        var segments = new List<QualifyingSegment>();

        foreach (var pair in shift.PunchPairs)
        {
            var rate = pair.Rate ?? 0;
            var pairSegments = rule.DayScheduleMode == DayScheduleMode.ConsecutiveDayRange
                ? ContinuousRangeQualifyingHoursCalculator.Segments(rule, pair, ctx)
                : PerDayQualifyingHoursCalculator.Segments(rule, pair, ctx);
            if (pairSegments.Count == 0)
            {
                continue;
            }

            segments.AddRange(pairSegments);
            var hrs = pairSegments.Sum(s => (decimal)(s.End - s.Start).TotalHours);
            qualifyingHours += hrs;

            if (rule.AdjustmentType == DifferentialAdjustmentType.FlatPerHour)
            {
                perHourAmount += hrs * rule.AdjustmentValue;
            }
            else if (rule.AdjustmentType == DifferentialAdjustmentType.Multiplier)
            {
                perHourAmount += hrs * rate * rule.AdjustmentValue;
            }
        }

        var amount = rule.AdjustmentType == DifferentialAdjustmentType.FixedBonus ? rule.AdjustmentValue : perHourAmount;
        return new CandidateMeasurement(qualifyingHours, amount, segments);
    }

    private static DifferentialCandidate ToCandidate(DifferentialRule rule, CandidateMeasurement measurement) =>
        new(rule, new AppliedDifferential
        {
            Code = rule.Code,
            Hours = measurement.Hours,
            Amount = measurement.Amount,
            AdjustmentType = rule.AdjustmentType,
            AdjustmentValue = rule.AdjustmentValue,
        });

    private static DifferentialEvaluation MissingPunchesEvaluation(DifferentialRule rule) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.ShiftHasMissingPunches,
        QualifyingHours = 0,
        Amount = 0,
        Segments = [],
        Explanation = "This shift has an incomplete punch pair, so no differential can be evaluated against it.",
    };

    private static DifferentialEvaluation NotEnabledEvaluation(DifferentialRule rule) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.NotEnabledByPayRule,
        QualifyingHours = 0,
        Amount = 0,
        Segments = [],
        Explanation = $"The pay rule in effect for this shift doesn't list '{rule.Code}' in its active differentials.",
    };

    private static DifferentialEvaluation NoOverlapEvaluation(DifferentialRule rule, Shift shift, PipelineContext ctx)
    {
        var zone = ctx.EmployeeTimeZone;
        var start = shift.PunchPairs.Where(p => p.HasInPunch)
            .Select(p => p.InPunch!.EffectiveTime.InZone(zone).Date).DefaultIfEmpty(shift.ShiftDate).Min();
        var end = shift.PunchPairs.Where(p => p.HasOutPunch)
            .Select(p => p.OutPunch!.EffectiveTime.InZone(zone).Date).DefaultIfEmpty(shift.ShiftDate).Max();

        var wasEverActive = DifferentialZoneProjector.Project(rule, start, end, ctx).Count > 0;
        return new DifferentialEvaluation
        {
            Code = rule.Code,
            Outcome = wasEverActive ? DifferentialOutcome.NoWindowOverlap : DifferentialOutcome.NotActiveOnAnyWorkedDay,
            QualifyingHours = 0,
            Amount = 0,
            Segments = [],
            Explanation = wasEverActive
                ? $"'{rule.Code}' was active on a day this shift worked, but none of the worked time fell inside its time-of-day window."
                : $"'{rule.Code}' isn't active on any day this shift worked.",
        };
    }

    private static DifferentialEvaluation BelowWindowThresholdEvaluation(DifferentialRule rule, CandidateMeasurement measurement) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.BelowMinHoursInWindow,
        QualifyingHours = measurement.Hours,
        Amount = 0,
        Segments = measurement.Segments,
        Explanation = $"Only {measurement.Hours:0.##}h qualified, but '{rule.Code}' requires at least {rule.MinHoursInWindow:0.##}h in a single shift.",
    };

    private static DifferentialEvaluation SupersededEvaluation(DifferentialRule rule, CandidateMeasurement measurement, string winnerCode) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.SupersededByExclusivityGroup,
        QualifyingHours = measurement.Hours,
        Amount = measurement.Amount,
        Segments = measurement.Segments,
        SupersededByCode = winnerCode,
        Explanation = $"'{rule.Code}' qualified for {measurement.Hours:0.##}h (${measurement.Amount:0.##}), but '{winnerCode}' pays more in the same exclusivity group, so only '{winnerCode}' applies.",
    };

    private static DifferentialEvaluation BelowRangeThresholdEvaluation(
        DifferentialRule rule, CandidateMeasurement measurement, LocalDate anchor, decimal occurrenceHours) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.BelowMinHoursInRange,
        QualifyingHours = measurement.Hours,
        Amount = 0,
        Segments = measurement.Segments,
        Explanation = $"This shift's {rule.DayOfWeekRangeStart}–{rule.DayOfWeekRangeEnd} occurrence (starting {anchor}) totaled {occurrenceHours:0.##}h, short of the {rule.MinHoursInRange:0.##}h '{rule.Code}' requires across the whole occurrence.",
    };

    private static DifferentialEvaluation AppliedEvaluation(DifferentialRule rule, CandidateMeasurement measurement) => new()
    {
        Code = rule.Code,
        Outcome = DifferentialOutcome.Applied,
        QualifyingHours = measurement.Hours,
        Amount = measurement.Amount,
        Segments = measurement.Segments,
        Explanation = $"Applied: {measurement.Hours:0.##}h qualified for ${measurement.Amount:0.##}.",
    };

    // Mirrors DifferentialApplier.FirstInstant/PremiumApplier.FirstInstant — the shift's earliest In
    // punch, used to resolve which PayRule (and therefore ActiveDifferentialCodes) is in effect.
    private static Instant FirstInstant(Shift shift) =>
        shift.PunchPairs
            .Where(p => p.HasInPunch)
            .Select(p => p.InPunch!.EffectiveTime)
            .DefaultIfEmpty(shift.ShiftDate.AtMidnight().InUtc().ToInstant())
            .Min();

    private readonly record struct CandidateMeasurement(decimal Hours, decimal Amount, List<QualifyingSegment> Segments);
}
