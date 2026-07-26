using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Model.PayRules;

namespace TimeCalculation.Api.Validation;

/// <summary>
/// Turns two PayResults (current vs draft pay rule, same punches) into the side-by-side diff
/// WhatIfResponse wants. Pure, no DB access — the "run it twice and diff" idea UI_PLAN.md §7
/// describes, made concrete. Matches shifts across the two runs by (ShiftDate, AnchorPunchId), the
/// same identity scheme PayLineItem/PremiumResult already use, so a UI can answer not just "gross
/// changed by $X" but "which shift, and why."
/// </summary>
public static class WhatIfDiffBuilder
{
    public static WhatIfResponse Build(
        WhatIfRequest request,
        PayRule currentRule, PayResult currentResult,
        PayRule draftRule, PayResult draftResult)
    {
        var currentShifts = currentResult.Workweeks.SelectMany(w => w.Shifts).ToDictionary(ShiftKey);
        var draftShifts = draftResult.Workweeks.SelectMany(w => w.Shifts).ToDictionary(ShiftKey);

        var diffs = currentShifts.Keys
            .Union(draftShifts.Keys)
            .OrderBy(k => k.ShiftDate)
            .ThenBy(k => k.AnchorPunchId)
            .Select(key => BuildShiftDiff(
                key,
                currentShifts.GetValueOrDefault(key),
                draftShifts.GetValueOrDefault(key)))
            .ToList();

        return new WhatIfResponse
        {
            EmployeeId = request.EmployeeId,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            Current = Summarize(currentRule, currentResult),
            Draft = Summarize(draftRule, draftResult),
            ShiftDiffs = diffs,
        };
    }

    private static (LocalDate ShiftDate, int AnchorPunchId) ShiftKey(ShiftPay shift) => (shift.ShiftDate, shift.AnchorPunchId);

    private static WhatIfShiftDiffResponse BuildShiftDiff(
        (LocalDate ShiftDate, int AnchorPunchId) key, ShiftPay? current, ShiftPay? draft)
    {
        var currentGross = current?.Gross ?? 0m;
        var draftGross = draft?.Gross ?? 0m;

        var status = current is null
            ? WhatIfShiftDiffStatus.OnlyInDraft
            : draft is null
                ? WhatIfShiftDiffStatus.OnlyInCurrent
                : LineItemsMatch(current.LineItems, draft.LineItems)
                    ? WhatIfShiftDiffStatus.Unchanged
                    : WhatIfShiftDiffStatus.Changed;

        return new WhatIfShiftDiffResponse
        {
            ShiftDate = key.ShiftDate,
            AnchorPunchId = key.AnchorPunchId,
            Status = status,
            CurrentLineItems = (current?.LineItems ?? []).Select(WhatIfLineItemResponse.FromDomain).ToList(),
            DraftLineItems = (draft?.LineItems ?? []).Select(WhatIfLineItemResponse.FromDomain).ToList(),
            CurrentGross = currentGross,
            DraftGross = draftGross,
            Delta = draftGross - currentGross,
        };
    }

    // Multiset comparison — order doesn't matter, and BaseRate/Multiplier are derived from
    // Hours/Amount/AdjustmentValue (see PayLineItem's own doc comment) so they can never disagree
    // independently of them.
    private static bool LineItemsMatch(IReadOnlyList<PayLineItem> a, IReadOnlyList<PayLineItem> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var aKeys = a.Select(LineItemKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var bKeys = b.Select(LineItemKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return aKeys.SequenceEqual(bKeys);
    }

    private static string LineItemKey(PayLineItem item) => $"{item.Type}|{item.Code}|{item.Hours}|{item.Amount}";

    private static WhatIfSummaryResponse Summarize(PayRule rule, PayResult result) => new()
    {
        PayRuleId = rule.Id,
        PayRuleName = rule.Name,
        PayRuleVersion = rule.Version,
        RegularHours = result.Workweeks.Sum(w => w.RegularHours),
        OvertimeHours = result.Workweeks.Sum(w => w.OvertimeHours),
        DoubletimeHours = result.Workweeks.Sum(w => w.DoubletimeHours),
        GrossPay = result.GrossPay,
    };
}
