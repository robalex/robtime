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
2. `PunchSubtypeInferrer` — classify each mid-shift Out→In gap as `Break` or `Lunch` (nearest of the
   `PayRule` expected lengths wins); a forced subtype is never overwritten.
3. `PunchPairer` — match In/Out into `PunchPair`s; split pairs at `PayRule`/`EmployeePosition`
   effective-date boundaries; incomplete (In-only / Out-only) pairs are intentional and surfaced.
4. `PairEnricher` — attach the effective `Position` + `Rate` to each pair.
5. `ShiftBuilder` — group pairs into `Shift`s (new shift when gap > `DistanceBetweenShiftsHours`).
6. `ShiftDater` — assign each `Shift` a calendar date per `PayRule.ShiftDateStrategy`.
7. `PremiumApplier` — run active `IPremiumRule`s (meal/rest) per shift; applied *after* the regular
   rate is known (premiums are excluded from it, so this is not circular).
8. `DifferentialApplier` — apply time-based `DifferentialRule`s per shift.
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

- `TimeCalculation` — the pure engine (only dependency: NodaTime).
- `TimeCalculation.Persistence` — EF Core + Npgsql code-first persistence, kept out of the engine.
  Model is validated by a build-only test; see its `README.md` for what's deferred.
- `TimeCalculationTests` — xunit.v3.

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
and `RecordedScenarioTests` pin end-to-end pay against hand-computed expected values.
