using NodaTime;

namespace TimeCalculation.Model.PayRules;

public class PayRule
{
    public int Id { get; set; }
    public int ClientId { get; set; }

    // Identity, naming (Gap H)
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Template lineage: what this rule was created from (e.g. a jurisdiction preset). Every field
    // stays editable after picking a template — this is provenance for showing which fields a
    // consumer has customised away from the template's values, not a constraint the model enforces.
    public string? TemplateCode { get; set; }
    public int? TemplateVersion { get; set; }

    // Versioning (Gap F): editing an Active rule never mutates this row — it creates a new row
    // with Version = this.Version + 1 and the same RuleFamilyId, so PayCalculationSnapshot's
    // (Id, Version) pairs stay meaningful and reproducible forever. RuleFamilyId is the stable
    // identity across a rule's edit history; by convention it equals the first version's own Id
    // (set by whoever creates that first version once its Id is known, since Id is DB-generated).
    // EffectiveFrom/EffectiveTo record when THIS VERSION was the active one — bookkeeping for the
    // version-history UI, not consulted by the calculation pipeline, which resolves the applicable
    // PayRule purely through PayRuleAssignment's own date range (see PipelineContext.GetRuleAt).
    public int RuleFamilyId { get; set; }
    public int Version { get; set; } = 1;
    public PayRuleStatus Status { get; set; } = PayRuleStatus.Draft;
    public LocalDate? EffectiveFrom { get; set; }
    public LocalDate? EffectiveTo { get; set; }

    // Punch inference
    public decimal PunchPairResetHours { get; set; } = 15;

    // Pairing
    public decimal MaxShiftLengthHours { get; set; } = 15;
    public decimal DistanceBetweenShiftsHours { get; set; } = 6;

    // Breaks & lunches — expected durations used by Stage 2 to classify
    // mid-shift Out→In gaps as Break or Lunch (nearest length wins)
    public int ExpectedBreakLengthMinutes { get; set; } = 15;
    public int ExpectedLunchLengthMinutes { get; set; } = 30;

    // Rounding
    public RoundingRule RoundingRule { get; set; } = new RoundingRule();

    // Shift dating
    public ShiftDateStrategy ShiftDateStrategy { get; set; } = ShiftDateStrategy.FirstPunchLocalDate;

    // State-specific premium rule codes active for this client (resolved via PremiumRegistry)
    public IReadOnlySet<string> ActivePremiumCodes { get; set; } = new HashSet<string>();

    // DifferentialRule.Code values this rule opts into, out of the client's full set of
    // (client-authored) differentials — same shape as ActivePremiumCodes, but selecting from a
    // client-owned collection rather than a fixed built-in registry.
    public IReadOnlySet<string> ActiveDifferentialCodes { get; set; } = new HashSet<string>();

    // Workweek definition (FLSA-required consistent 168-hr window)
    public IsoDayOfWeek WorkweekStartDay { get; set; } = IsoDayOfWeek.Sunday;

    // Pay period definition (Phase 6 prerequisite — UI_PLAN.md §9 decision 18: lives here rather
    // than on ClientSettings so a frequency change gets the same effective-dating/versioning as any
    // other rule change). PayPeriodAnchor is only consulted for Weekly/BiWeekly — SemiMonthly and
    // Monthly derive their boundaries from the calendar and ignore it entirely; see
    // PayPeriodCalculator's own doc comments. Its default (LocalDate's own default,
    // 0001-01-01) matches PayPeriodCalculator.ContainingDate's `anchor = default` parameter default,
    // so an un-set anchor still resolves to a real, consistent period boundary rather than needing
    // null-handling — a client running Weekly/BiWeekly should still set a real reference date once
    // one is known, since 0001-01-01 has no relationship to their actual pay calendar.
    public PayPeriodFrequency PayPeriodFrequency { get; set; } = PayPeriodFrequency.BiWeekly;
    public LocalDate PayPeriodAnchor { get; set; }

    // Overtime
    public OvertimeRule OvertimeRule { get; set; } = new OvertimeRule();

    public bool IsDeleted { get; set; }
}
