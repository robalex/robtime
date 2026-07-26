using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Phase 4 §7's "down payment" (UI_PLAN.md): pick one employee, pick one past period, run both
/// pay rule configs synchronously, show a side-by-side line-item diff. PeriodEnd is exclusive,
/// matching the wire convention used elsewhere for date ranges.
/// </summary>
public record WhatIfRequest
{
    public required int EmployeeId { get; init; }
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }
}

public record WhatIfLineItemResponse
{
    public required PayLineType Type { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Amount { get; init; }
    public decimal? BaseRate { get; init; }
    public decimal? Multiplier { get; init; }

    public static WhatIfLineItemResponse FromDomain(PayLineItem item) => new()
    {
        Type = item.Type,
        Code = item.Code,
        Description = item.Description,
        Hours = item.Hours,
        Amount = item.Amount,
        BaseRate = item.BaseRate,
        Multiplier = item.Multiplier,
    };
}

public enum WhatIfShiftDiffStatus
{
    /// <summary>Same line items (by Type/Code/Hours/Amount) in both runs.</summary>
    Unchanged,

    /// <summary>The shift exists in both runs, but at least one line item differs.</summary>
    Changed,

    /// <summary>The shift's pay only shows up under the current configuration — e.g. a difference
    /// in ShiftDateStrategy or PunchPairResetHours grouped the same punches differently.</summary>
    OnlyInCurrent,

    /// <summary>The shift's pay only shows up under the draft configuration.</summary>
    OnlyInDraft,
}

public record WhatIfShiftDiffResponse
{
    public required LocalDate ShiftDate { get; init; }
    public required int AnchorPunchId { get; init; }
    public required WhatIfShiftDiffStatus Status { get; init; }
    public required List<WhatIfLineItemResponse> CurrentLineItems { get; init; }
    public required List<WhatIfLineItemResponse> DraftLineItems { get; init; }
    public required decimal CurrentGross { get; init; }
    public required decimal DraftGross { get; init; }
    public required decimal Delta { get; init; }
}

/// <summary>Period totals for one of the two runs — which PayRule produced them, and the FLSA
/// hour/gross breakdown summed across every workweek the period touched.</summary>
public record WhatIfSummaryResponse
{
    public required int PayRuleId { get; init; }
    public required string PayRuleName { get; init; }
    public required int PayRuleVersion { get; init; }
    public required decimal RegularHours { get; init; }
    public required decimal OvertimeHours { get; init; }
    public required decimal DoubletimeHours { get; init; }
    public required decimal GrossPay { get; init; }
}

public record WhatIfResponse
{
    public required int EmployeeId { get; init; }
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }
    public required WhatIfSummaryResponse Current { get; init; }
    public required WhatIfSummaryResponse Draft { get; init; }
    public required List<WhatIfShiftDiffResponse> ShiftDiffs { get; init; }
}
