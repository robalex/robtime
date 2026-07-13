# Performance & Correctness Fix Plan

Self-contained work plan from the scale review (50k employees × 1 year). Work through items in
order — correctness first, then performance. Each item lists the files, the exact change, and the
tests to add. **Definition of done for every item: `dotnet test` fully green (196 existing tests
must not regress except where an item explicitly says behavior changes), plus the new tests listed.**

Conventions used throughout the codebase (do not violate):
- Pipeline stages are pure: immutable input, new output, no mutation of arguments.
- Money/hours are `decimal`. Times are NodaTime `Instant`.
- Commit per item (or per pair of related items) with a message explaining *why*.

---

## Part 1 — Correctness

### 1.1 Coverage-gap policy: add `TryGetRuleAt`, keep `GetRuleAt` throwing

**Problem:** `PipelineContext.GetRuleAt` (TimeCalculation/Pipeline/PipelineContext.cs, ~line 38)
throws when no `PayRuleAssignment` covers a date. In a future batch run, one employee with an
assignment gap would kill the whole batch. The engine should keep failing fast, but the batch
layer needs a non-throwing probe.

**Change:**
- In `PipelineContext`, add:
  ```csharp
  public bool TryGetRuleAt(Instant time, out PayRule rule)
  ```
  Same lookup loop as `GetRuleAt` but returns `false` instead of throwing. Refactor `GetRuleAt`
  to call it and throw on `false` (single copy of the loop).
- XML-doc both: engine stages call `GetRuleAt` (a gap is invalid input); orchestration/batch code
  should probe with `TryGetRuleAt` before enqueueing an employee-period.

**Tests (PipelineContextTests.cs):**
- `TryGetRuleAt_OutsideCoverage_ReturnsFalse`
- `TryGetRuleAt_InsideCoverage_ReturnsTrueAndRule`
- `GetRuleAt_OutsideCoverage_Throws` (pin existing behavior explicitly)

### 1.2 `FixedHours` participation in the regular rate

**Problem:** PLAN.md says FixedHours "contributes to total-hours-for-regular-rate per FLSA rules
where applicable" — never implemented. `RegularRateCalculator` ignores FixedHours entirely
(hours not in denominator, no earnings in numerator), while `PaySummarizer` pays them at minimum
wage. For FixedHours entries that represent *worked* time, RROP is overstated.

**Change:**
- `TimeCalculation/Model/Punch.cs`: add
  ```csharp
  /// <summary>On FixedHours punches: when true, the hours count as hours worked for the
  /// FLSA regular rate (denominator) and their pay enters the numerator. Default false
  /// (e.g. paid leave, which is not "hours worked").</summary>
  public bool CountsTowardRegularRate { get; init; }
  ```
- `TimeCalculation/Calculation/RegularRateCalculator.cs`: change signature to
  `Calculate(Workweek week, decimal minimumWage)`. For each shift's FixedEntries where
  `Kind == FixedHours && CountsTowardRegularRate`: add `entry.Hours ?? 0` to `hours` and
  `(entry.Hours ?? 0) * minimumWage` to `straight` (minimum wage matches how PaySummarizer
  values these entries — keep the two consistent).
- `TimeCalculation/Calculation/PayCalculator.cs` (`CalculateWorkweekPay`): pass
  `ctx.Employee.MinimumWage`.
- Update the one other caller: `RetroactiveBonusRecalculator` calls
  `RegularRateCalculator.Calculate(week)` — add a `decimal minimumWage` parameter to
  `Recalculate(...)` and thread it through. Update its tests.

**Tests (RegularRateCalculatorTests.cs):**
- `FixedHours_DefaultFlagFalse_ExcludedFromRegularRate` (pins today's behavior)
- `FixedHours_CountsTowardRegularRate_AddsHoursAndEarnings` — e.g. 10 clock hrs @ $20 + 5 fixed
  hrs (flag true, min wage $15) → RROP = (200 + 75) / 15.
- Existing tests: update calls for the new parameter; expected values unchanged.

### 1.3 Consistent rule resolution at Out→In gap boundaries

**Problem:** `PunchSubtypeInferrer` classifies a mid-shift gap using the rule at the **prior Out**
(gap start); `ShiftBuilder` decides shift membership using the rule at the **next pair's In**
(gap end). If `DistanceBetweenShiftsHours` changes at a boundary between them, one component can
call the gap a lunch while the other calls it a shift boundary → a "new shift" that starts with a
Lunch-subtyped punch, which premium rules would misread as a meal taken.

**Change:** `TimeCalculation/Pipeline/ShiftBuilder.cs` (~line 39): resolve the rule at the gap
start, i.e. `ctx.GetRuleAt(lastOut.EffectiveTime)` when `lastOut` is non-null, falling back to
`pair.InPunch.EffectiveTime` when there is no prior Out. This matches PunchSubtypeInferrer.
Update the class doc comment to state: *gap-related decisions always use the rule active at the
gap's start.*

**Tests (ShiftBuilderTests.cs):**
- `GapSpanningRuleChange_UsesRuleAtGapStart` — two `PayRuleAssignment`s where
  `DistanceBetweenShiftsHours` changes at midnight inside an Out→In gap; assert the shift
  split/join decision matches the gap-start rule. Also assert (in a paired test or the same one)
  that `PunchSubtypeInferrer` + `ShiftBuilder` agree: a gap classified Break/Lunch never becomes
  a shift boundary for the same input.

### 1.4 Premium anchor identity when a shift starts with a synthetic punch

**Problem:** `PunchPairer.SplitAtBoundaries` creates interior sub-pair punches with `Id = 0`.
`PremiumApplier.AnchorPunchId(shift)` uses the shift's earliest In punch id. If shift dating ever
places a split pair's *second* half at the start of a shift, the anchor becomes 0 and the premium
waive/override identity `(AnchorPunchId, Code)` collides across shifts.

**Change:**
- First write the test below; if it fails, fix `AnchorPunchId` in
  `TimeCalculation/Pipeline/PremiumApplier.cs`: prefer the earliest In punch **with `Id != 0`**;
  only fall back to 0 when no real punch exists. Document the fallback in the method comment.

**Tests (PremiumApplierTests.cs):**
- `AnchorPunchId_ShiftStartingWithSyntheticPunch_UsesFirstRealPunchId` — construct a shift whose
  first pair has a synthetic In (`Id = 0`) and a later pair with a real id; assert the premium's
  `AnchorPunchId` is the real id.

---

## Part 2 — Performance

### 2.1 Binary search in `PipelineContext` lookups

**Problem:** `GetRuleAt` / `GetPositionAt` / `GetBoundaryInstantsBetween` linearly scan the
assignment lists; they're called per punch / pair / shift / day, so cost is O(P × A).

**Change (TimeCalculation/Pipeline/PipelineContext.cs):**
- Both lists are already sorted by `EffectiveFrom` in the constructor. Replace the reverse linear
  scans in `GetRuleAt`/`TryGetRuleAt` and the date-based branch of `GetPositionAt` with a binary
  search for the last assignment whose `EffectiveFrom <= date`, then check its `EffectiveTo`.
  Write one private static helper used by both:
  ```csharp
  private static T? FindEffective<T>(IReadOnlyList<T> sorted, LocalDate date,
      Func<T, LocalDate> from, Func<T, LocalDate?> to) where T : class
  ```
- The positionIdOverride branch of `GetPositionAt` stays a linear scan (it matches on Id, list is
  tiny).
- `GetBoundaryInstantsBetween` may stay linear (called once per pair, lists are short) — leave it.

**Semantic caution:** the current reverse scan returns the **latest** matching assignment when
ranges overlap. Binary search for "last EffectiveFrom <= date" preserves that only if you check
just that one candidate. Preserve current semantics exactly; the existing PipelineContextTests
must pass unmodified.

**Tests:** existing suite covers behavior; add one overlapping-assignment test pinning
"latest EffectiveFrom wins" before refactoring, so the refactor is provably behavior-preserving.

### 2.2 Compute `ShiftAnalysis` once per shift

**Problem:** every premium rule calls `ShiftAnalysis.From(shift)` in both `Applies` and
`Calculate` → 2 × rules full rebuilds per shift (see CaMealPremiumRule.cs:20,24 and the same
pattern in all six rules).

**Change:**
- `TimeCalculation/Calculation/Premiums/IPremiumRule.cs`: change both methods to
  ```csharp
  bool Applies(ShiftAnalysis analysis, PremiumContext ctx);
  PremiumResult Calculate(ShiftAnalysis analysis, PremiumContext ctx);
  ```
  (Rules only use the analysis today — drop the raw `Shift` from the signature. If a future rule
  needs the shift, `ShiftAnalysis` can grow a reference then.)
- Update `PremiumRuleBase` and all six rules: delete their internal `ShiftAnalysis.From` calls,
  use the parameter.
- `TimeCalculation/Pipeline/PremiumApplier.cs`: build `var analysis = ShiftAnalysis.From(shift);`
  once per shift, pass to `Applies`/`Calculate`.
- Update PremiumRulesTests to build a `ShiftAnalysis` (via `ShiftAnalysis.From(Build(...))`) —
  mechanical change, assertions unchanged.

### 2.3 Binary search for nearest shift in `ShiftBuilder.AttachFixedEntries`

**Problem:** `FindNearestShiftIndex` (ShiftBuilder.cs:85) scans every punch of every shift per
fixed entry → O(F × S × P).

**Change:** shifts are built in time order. Precompute once per call an array of
`(startInstant, endInstant)` per shift (first In's EffectiveTime, last Out's — fall back to the
other punch when one is missing). For each fixed entry, binary-search the starts for the entry
time's insertion point and compare distance to the candidate shift before/after (distance = 0 if
inside the shift's range). Same nearest-shift answer, O(F log S).

**Tests (ShiftBuilderTests.cs):** existing fixed-entry tests must pass; add
`FixedEntry_BetweenTwoShifts_AttachesToNearer` and `FixedEntry_InsideAShift_AttachesToThatShift`
if not already present.

### 2.4 Memoize `TotalHours` on `Shift`, `WorkDay`, `Workweek`

**Problem:** `Workweek.TotalHours` → `WorkDay.TotalHours` → `Shift.TotalHours` →
`PunchPair.TotalHours` recomputes the whole tree on every access; overtime rules and RROP hit
these repeatedly.

**Change:** in `Shift`, `WorkDay`, `Workweek` (all init-only records), replace the expression-body
property with a lazily cached field:
```csharp
private decimal? _totalHours;
public decimal TotalHours => _totalHours ??= PunchPairs.Sum(p => p.TotalHours);
```
**Do NOT memoize `PunchPair.TotalHours`** — `PunchPair.OutPunch` has a setter and is mutated
during pairing; caching there would return stale values. Add a comment on PunchPair saying so.

**Tests:** existing suite covers values. Add one test asserting a `with`-mutated copy recomputes
(e.g. `shift with { PunchPairs = ... }` reports the new total) — record `with` copies fields, so
verify the cache field doesn't leak a stale value into the copy. If it does (records copy private
fields), switch to computing eagerly in the property once and storing under a lock-free pattern:
simplest correct alternative is an explicit `decimal TotalHours { get; init; }` set by the
builders — choose whichever passes the `with` test cleanly.
> Note: C# record `with` DOES copy private backing fields — the lazy-field approach WILL leak the
> cache into copies. Since all mutation happens via `with`, prefer the safer form: compute in the
> grouping/builder stage and assign an init property, or keep the lazy field but reset it is not
> possible — so use init-time computation for `WorkDay`/`Workweek` (built once by groupers) and
> leave `Shift.TotalHours` computed-on-access but cached only if the `with` test passes.
> Practical resolution: `WorkDay`/`Workweek` are never `with`-mutated after construction (verify
> by grep); `Shift` IS (`with { Differentials/Premiums/ShiftDate }` — those don't change hours,
> so a leaked cache is still *correct*). Document this invariant in a comment: any future `with`
> that changes `PunchPairs` must construct a fresh Shift instead.

**Tests:** `TotalHours_AfterWithOnPunchPairs_Recomputes` — if it can't be made to pass with the
lazy field, construct fresh (per the note) and make the test assert the documented invariant
instead.

### 2.5 (Optional — do last, skip if contract friction) Single up-front sort

**Problem:** `PunchSubtypeInferrer`, `PunchPairer`, `ShiftBuilder` each `OrderBy` their input;
`WorkweekGrouper` sorts days. Input is already ordered after the first sort.

**Decision needed before doing this:** stage tests deliberately pass unordered input (e.g.
`DaysProvidedOutOfOrder_AreSortedWithinWeek`), i.e. sorting is part of each stage's public
contract. Removing it is a breaking contract change for marginal gain (three O(n log n) sorts).
**Recommendation: skip.** If done anyway: sort once in `PayCalculator.PrepareShifts`, remove
stage-level `OrderBy`s, update stage doc comments to state the precondition, and update the tests
that relied on stage-level sorting.

---

## Part 3 — Explicitly out of scope (do not attempt)

- Snapshot persistence / calculate-once-read-many reporting layer (Phase 8 worker-queue decision
  is still open; `PayCalculationSnapshot` model already exists).
- Bulk-ingest, partitioning, EF migrations (need a live Postgres).
- Any change to premium legal logic (waiver policies for PR/OR/WA are flagged pending legal
  review).

## Verification (after every item)

```bash
dotnet build RobTime.sln          # zero errors (nullable warnings in older stages are pre-existing)
dotnet test                       # all green — 196 existing + new tests from the item
```

Commit per item with a why-focused message. When all items are done, run the full suite one final
time and update this file: mark each item ☑ with the commit hash.
