# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the whole solution (TimeCalculation, TimeCalculation.Persistence, TimeCalculationTests)
dotnet build

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~PunchPairerTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~PunchPairerTests.InThenOut_ProducesOnePair"
```

## Code Style & Conventions

Living list — expect more entries over time. These apply to new/edited code in this repo; existing
code isn't retroactively rewritten to match just because a rule was added after the fact.

**Avoid:**
- **Multiple `return` statements from a method.** Prefer a single return — often by computing a
  result and returning it once at the end, or by giving the different outcomes a proper class/record
  type instead of returning early from several branches. (Exception in practice: ASP.NET Core
  minimal-API endpoint handlers returning `Results<T1, T2, ...>` unions — validation/not-found/success
  branches each need their own `return` to produce a different union member. Flag it if you're unsure
  whether a given method counts as this kind of exception.)
- **Tuples** (`(int, string)`, named tuples, `ValueTuple`, etc.). If one genuinely seems like the
  right shape, ask first rather than reaching for it — a record is usually the actual answer.
- **Anonymous types** (`new { Foo = 1, Bar = 2 }`). Same rule: ask first rather than reaching for one.

**Preferences:**
- **Always use braces**, even for a single-line `if`/`for`/`foreach`/`while`/etc. body — span it
  across multiple lines rather than writing `if (x) DoThing();` on one line. Backed by
  `.editorconfig`'s `csharp_prefer_braces = true` (IDE0011), so the IDE/`dotnet format` will suggest
  this automatically — note that doesn't fail `dotnet build` today (`EnforceCodeStyleInBuild` isn't
  set), so it won't turn into a CI break on its own.

**Tooling:**
- **No object-mapping library** (AutoMapper, Mapperly, etc.) — hand-written mapping/extension
  methods, even for entity ↔ DTO. Decided 2026-07-23 when the question came up for the upcoming
  response-DTO work (Gap C); revisit if that gets big/tedious enough to reconsider, but don't add
  one by default.

**Architecture:**
- **No business logic or database access directly in endpoints/controllers.** An endpoint's job is
  translating HTTP ↔ a call into a service — nothing else. The actual work (querying/persisting via
  `PayrollDbContext`, orchestrating multiple steps) belongs in a service class instead
  (`TimeCalculation.Api/Services/`).
- **Services should delegate real business logic to plain classes with no DB dependency**, so that
  logic is unit-testable without a database or EF Core mocking. Shape: a service *orchestrates*
  (fetch → decide → persist); the "decide" part should be a method on a class that only takes and
  returns plain data — no `PayrollDbContext`, no `IQueryable`. Keep this distinct from the
  `TimeCalculation` engine project: API-layer business rules (request validation, cross-field
  invariants, "does this request make sense") belong in `TimeCalculation.Api`, not in the
  calculation engine, which is reserved for the actual pay-calculation pipeline (see Architecture
  below) — don't blur that boundary just because both are "business logic."
- General north star: prefer SOLID and clean-code principles over expedience — single-responsibility
  types, depend on abstractions rather than concretions where it genuinely helps testability, small
  well-named methods over long ones.

  *Currently out of compliance, not yet fixed:* all four existing endpoints
  (`ClientEndpoints`/`EmployeeEndpoints`/`PayRuleEndpoints`/`PunchEndpoints`) inject `PayrollDbContext`
  directly and call it inline. Flagged rather than silently left — ask before assuming whether/when
  to refactor these versus applying the rule fresh starting with the next endpoint work.

## Architecture

RobTime is a payroll time-calculation library: raw clock punches in, an itemized `PayResult` out.
It is a **pipeline of pure stages** — each stage is a static class with an `Execute(input, ctx)`
method taking immutable input and returning immutable output; no stage mutates its input or reaches
across stages. `PipelineContext` carries the effective-dated configuration and resolves the right
`PayRule`/`Position` for any instant via `GetRuleAt` / `GetPositionAt`, so mid-period rule changes
never require re-running the pipeline.

### The pipeline (`TimeCalculation/Pipeline/`)

Classes are named for what they do; the `// Stage N —` doc comment on each records its position in
the plan's 13-stage design (see `PLAN.md`).

1. `PunchRounder` — apply `PayRule.RoundingRule` to punch times (raw + rounded both kept).
2. `PunchPairer` — match In/Out into `PunchPair`s; split pairs at `PayRule`/`EmployeePosition`
   effective-date boundaries; incomplete (In-only / Out-only) pairs are intentional and surfaced.
3. `PairPositionAndRateAttacher` — attach the effective `Position` + `Rate` to each pair.
4. `ShiftBuilder` — group pairs into `Shift`s (new shift when gap > `DistanceBetweenShiftsHours`).
5. `PunchSubtypeInferrer` — runs after shifts are built, so shift boundaries are already settled;
   classifies each Out→In gap *between two PunchPairs already in the same Shift* as `Break` or
   `Lunch` (nearest of the `PayRule` expected lengths wins) without re-deriving the boundary
   decision itself; a forced subtype is never overwritten.
6. `ShiftDater` — assign each `Shift` a calendar date per `PayRule.ShiftDateStrategy`
   (first-punch / last-punch / majority-hours). `SplitAtMidnight` instead splits a midnight-crossing
   shift into one shift per day via `MidnightShiftSplitter`, so this stage can return more shifts
   than it received.
7. `PremiumApplier` — run active `IPremiumRule`s (meal/rest) per shift; applied *after* the regular
   rate is known (premiums are excluded from it, so this is not circular).
8. `DifferentialApplier` — apply time-based `DifferentialRule`s per shift. A rule's active days come
   from a single `DayScheduleMode` (EveryDay / DaysOfWeek / ConsecutiveDayRange / SpecificDates /
   Holidays) — mutually exclusive, not ANDed filters.
   - `RangeDifferentialQualifier` (Stage 8b) then runs on the full shift list: a `ConsecutiveDayRange`
     rule with `MinHoursInRange` can only be judged once a whole range occurrence is visible (e.g.
     one Thursday→Tuesday block, independent of the payroll week), so it strips differentials whose
     range-summed qualifying hours fall short — before the regular rate (Stage 11) or line items
     (Stage 13) read them.
9. `WorkDayGrouper` — bucket shifts into `WorkDay`s by date.
10. `WorkweekGrouper` — group days into FLSA `Workweek`s per `PayRule.WorkweekStartDay`, numbering
    consecutive days for the 7th-day overtime rule.

### Calculators (`TimeCalculation/Calculation/`)

- `PayCalculator` — the orchestrator: runs the stages end-to-end and returns a `PayResult`. It is a
  coordinator, not a doer. Deterministic (same inputs → equal result).
- `RegularRateCalculator` — Stage 11 weighted FLSA regular rate per workweek.
- `Overtime/` — Stage 12: `IOvertimeRule` strategies (`FederalOvertimeRule`, `CaliforniaOvertimeRule`,
  selected by `OvertimeRuleFactory`), producing both premium-view and composed-view pay.
- `Premiums/` — the `IPremiumRule` framework, six state rules, `PremiumRegistry`, `WaiverEvaluator`,
  and `ShiftAnalysis` (reconstructs breaks/meals from punch subtypes).
- `PaySummarizer` — Stage 13: turns a workweek's pieces into a per-shift breakdown of itemized
  `PayLineItem`s, so a UI can show why each shift/pair was paid the way it was. Regular pay is
  itemized per punch pair (its own rate); overtime premium is attributed to whichever pair(s) it
  falls on by a "hours accrue toward OT in the order worked" convention (see the class doc comment)
  — it decides *which* shift/pair the already-computed premium total lands on, never how much is owed.
- `PayPeriodCalculator`, `RetroactiveBonusRecalculator` — pay-period boundaries and FLSA §778.209
  retroactive bonus recalculation.

### Projects

Layered so persistence and (eventually) an API each depend only on what they actually need:

```
TimeCalculation.Model  (entities/config; only dependency: NodaTime)
      ^                          ^
      |                          |
TimeCalculation            TimeCalculation.Persistence
(pipeline + calculators;         (EF Core + Npgsql)
 depends on Model)
```

- `TimeCalculation.Model` — every type under `TimeCalculation.Model` / `.PayRules` / `.Premiums`
  namespaces (`Punch`, `PayRule`, `Position`, `Shift`, `Workweek`, `PayResult`, `PunchAuditEntry`,
  etc.) plus config enums. Pure data, no engine logic, only dependency NodaTime. Both the engine and
  Persistence reference it directly; neither references the other.
- `TimeCalculation` — the pure calculation engine (pipeline stages, calculators, premium/overtime
  rules). References `TimeCalculation.Model` for the shapes it operates on.
- `TimeCalculation.Persistence` — EF Core + Npgsql code-first persistence. References only
  `TimeCalculation.Model` — it stores entities, it doesn't run `PayCalculator`. Model is validated
  by a build-only test; see its `README.md` for what's deferred.
- `TimeCalculationTests` — xunit.v3.

A future API/worker project would reference `TimeCalculation.Model` (shapes), `TimeCalculation.Persistence`
(storage), and `TimeCalculation` (to actually calculate pay) — no project needs to depend on more
than its job requires.

### Key types

- `Punch` — a clock/fixed event; `PunchTime` is a NodaTime `Instant`, `EffectiveTime` prefers the rounded time.
- `PunchKind` — `In`, `Out`, `FixedDollar`, `FixedHours`; `PunchSubtype` — `None`/`Break`/`Lunch`.
- `PunchPair` → `Shift` → `WorkDay` → `Workweek` — the grouping hierarchy (all immutable records).
- `PayRule` (+ `RoundingRule`, `OvertimeRule`) — per-client config; effective-dated via `PayRuleAssignment`.
- `Position` / `EmployeePositionAssignment` — effective-dated pay rates (multi-rate weeks supported).
- `PayResult` / `WorkweekPay` / `ShiftPay` / `PayLineItem` — the itemized output, drillable
  week → shift → line item; `PayLineItem.ShiftDate`/`AnchorPunchId` identify its owning shift
  (same identity scheme as `PremiumResult`, sourced from `Shift.AnchorPunchId`).
  `PayCalculationSnapshot` freezes it for audit.

### Testing conventions

Tests use xunit.v3. `TestEntityCreator` is the shared factory for `Punch`/`PunchPair` test objects
and `PipelineContext`. NodaTime `Instant.FromUtc(...)` constructs punch times throughout. Beyond
per-stage tests, `PropertyBasedTests` assert purity/idempotency/invariants over seeded random inputs,
`RecordedScenarioTests` pin end-to-end pay against hand-computed expected values, and
`EndToEndTests` is the broader confidence suite — one test per feature area (pairing/orphans,
overtime, effective dating, rounding, subtype-driven premiums, differentials, all six state
premiums, DST, retroactive bonus), always starting from raw `Punch`es through `PayCalculator.Calculate`.
It's what found three real crash bugs (`PairPositionAndRateAttacher`/`ShiftBuilder`/`ShiftDater` all
unconditionally dereferenced `PunchPair.InPunch`, which is null for an orphan Out) that no
per-stage unit test caught, because none of them chained stages together.
