# Time & Attendance Calculation Engine — Plan

## 1. Goals & Non-Goals

### In scope
- Pure calculation engine: punches → pay
- Hourly employees only (salary deferred)
- Configurable per-jurisdiction pay rules (federal, CA, CO, OR, WA, PR)
- Inferred punch typing (no In/Out hint from employee)
- Multi-rate within a workweek (positions with their own rates)
- Daily + weekly + 7th-day overtime, configurable per `PayRule`
- Weighted regular-rate-of-pay including non-discretionary bonuses, differentials
- State-specific meal/rest premiums with supervisor/employee overrides
- Time-based differentials with recurrence (e.g. weekend, holiday, night-shift)
- Effective-dated pay rules (mid-period promotion/demotion supported)
- Punch rounding with configurable rules
- All times stored UTC; timezone metadata on employee & punch
- Audit trail on every punch
- Persistence via EF Core code-first on PostgreSQL

### Out of scope (deferred)
- Salaried / exempt employees
- HTTP API, UI, calendar visualization
- PTO/sick/vacation accrual & balance tracking (leave-as-fixed-hours is fine)
- Tip credits / tipped wages
- Multi-currency (USD only)
- Payroll-tax computation, garnishments, deductions, W-2/1099 generation
- Scheduling (we calculate from actuals, not schedules)
- Geofencing / location validation
- Direct deposit / payment processing

---

## 2. Architecture Overview

The engine is a **pipeline of pure stages**. Each stage takes immutable input and produces immutable output. No stage reaches across stages or mutates inputs. This makes every stage independently testable and the whole flow deterministic.

```
Raw punches (with audit + timezone)
    │
    ▼
[ 0. Ingest        ] — persist, audit, timezone tag
    │
    ▼
[ 1. Round         ] — apply PayRule rounding to punch times
    │
    ▼
[ 2. Infer types   ] — derive In/Out from sequence + reset window
    │
    ▼
[ 3. Pair          ] — In+Out → PunchPair (skip FixedDollar/FixedHours)
    │
    ▼
[ 4. Enrich pairs  ] — total hours, attach position/rate
    │
    ▼
[ 5. Build shifts  ] — group adjacent pairs into Shift
    │
    ▼
[ 6. Date shifts   ] — assign each Shift to a calendar date (PayRule-driven)
    │
    ▼
[ 7. Premiums      ] — apply meal/rest penalty rules per Shift
    │
    ▼
[ 8. Differentials ] — apply time-based pay differentials per pair/shift
    │
    ▼
[ 9. Group → Days  ] — Shifts → Day buckets
    │
    ▼
[10. Group → Weeks ] — Days → Workweek (per PayRule anchor)
    │
    ▼
[11. Regular rate  ] — weighted RROP per workweek
    │
    ▼
[12. Overtime      ] — daily, weekly, 7th-day per PayRule
    │
    ▼
[13. Summarize     ] — line-item PayResult per PayPeriod
```

### Design principles
- **Pure functions, immutable data**: stages are `Func<TIn, TOut>`. C# `record` types throughout.
- **Strategy pattern for rules**: `IPremiumRule`, `IOvertimeRule`, `IDifferentialRule`, `IRoundingRule` — registered by jurisdiction, selected via `PayRule`.
- **No god calculator**: `PayCalculator` is an orchestrator, not a doer. Each stage is its own class.
- **Snapshot the inputs**: every `PayResult` carries the inputs and the rule versions used, so recalculation is reproducible and disputes are auditable.

---

## 3. Domain Model

### Core entities

| Entity | Purpose | Key fields |
|---|---|---|
| `Punch` | Raw clock event | `Instant`, `PunchKind` (In/Out/FixedDollar/FixedHours), `EmployeeId`, `PositionId?`, `TimeZoneId`, `Amount?`, `Hours?` |
| `PunchPair` | Matched In+Out | `InPunch`, `OutPunch`, `Position`, `Rate`, `TotalHours` (computed) |
| `Shift` | Contiguous pairs | `PunchPairs`, `Position`, `ShiftDate`, `Differentials`, `Premiums` |
| `WorkDay` | All shifts on a date | `Date`, `Shifts`, `TotalHours` |
| `Workweek` | 168 hr OT window | `StartInstant`, `Days`, `RegularRate`, `OvertimeHours` |
| `PayPeriod` | Pay-cycle bucket | `Start`, `End`, `Frequency` (weekly/bi-weekly/semi-monthly/monthly), `Workweeks` |
| `PayResult` | Final output | line items: base, differentials, bonuses, premiums, OT premium, total |

Money fields: **`decimal`** (`numeric(19,4)` in Postgres). Hours: `decimal` too — never mix `double` and `decimal` in money math.

### Punch kinds (replaces current `PunchType` enum)
- `In`, `Out` — clock punches, get paired
- `FixedDollar` — flat dollar add (e.g. tool reimbursement, attendance bonus); skips pairing
- `FixedHours` — flat hours add at a given rate (e.g. paid bereavement, manual adjustment); skips pairing, contributes to total-hours-for-regular-rate per FLSA rules where applicable

### Configuration entities

| Entity | Purpose |
|---|---|
| `PayRule` | Versioned bundle: rounding, OT rules, shift-date strategy, max shift length, distance between shifts, workweek anchor day |
| `PayRuleAssignment` | Links `(Employee, PayRule, EffectiveFrom, EffectiveTo)` — effective-dated |
| `Position` | Job code + base rate; one employee can have many |
| `EmployeePosition` | `(Employee, Position, EffectiveFrom, EffectiveTo, Rate)` — also effective-dated |
| `RoundingRule` | "Round to M minutes if within N minutes of pivot"; multiple strategies supported |
| `OvertimeRule` | Daily thresholds (8/12 etc.), weekly threshold (40), 7th-consecutive-day rule |
| `DifferentialRule` | RRULE-style recurrence + amount/multiplier + min-hours-to-qualify + optional bonus |
| `PremiumRule` | State-specific penalty (CA meal, CO rest, etc.) + override acceptance config |

### Reference & audit entities

| Entity | Purpose |
|---|---|
| `Employee` | Identity, home `TimeZoneId`, current state |
| `Client` (employer) | Owns pay rules, positions, etc. |
| `StateMinimumWage` | `(State, EffectiveFrom, EffectiveTo, Amount)` lookup |
| `HolidayCalendar` | Federal + per-state observed holidays, used by `DifferentialRule` |
| `PunchAudit` | Every create/edit/delete of a punch: who, when, before, after, reason |
| `Override` | `SupervisorOverride` or `EmployeeWaiver` keyed to a specific premium occurrence |
| `PayCalculationSnapshot` | The frozen `PayResult` + the IDs/versions of every rule used |

---

## 4. The Calculation Pipeline (stage detail)

### Stage 0 — Ingest
Persist the raw punch with `CreatedBy`, `CreatedAt`, employee's `TimeZoneId`, and the punch's own `TimeZoneId` (default = employee's, override possible). Never edit in place — produce a new `PunchAudit` on any change. Soft delete only.

### Stage 1 — Round
Apply `PayRule.RoundingRule` (default: no-op). Common rule: "round to nearest 15 minutes when within 7 minutes of the quarter hour" (7/8 rule). Implementations: `NoRounding`, `NearestMinutes`, `QuarterHourWithGracePeriod`, `RoundUpToMinutes` (custom). Store both raw and rounded times.

### Stage 2 — Infer punch type (In/Out)
For non-FixedDollar/FixedHours punches:
1. Look up the employee's **most recent prior** punch.
2. If none, this is `In`.
3. If most recent was `Out`, this is `In`.
4. If most recent was `In`:
   - If `(NewPunch.Time - LastPunch.Time) > PayRule.PunchPairResetHours` (default **15 hrs**, configurable), then the last `In` is treated as **orphan** (logged for supervisor review), and this punch is `In`.
   - Otherwise this is `Out`.
5. Allow supervisors to override the inference manually; the override is itself audited.

### Stage 3 — Pair
Existing logic mostly correct. Tighten:
- If an `Out` arrives without a matching `In` → orphan `Out` (record but don't pair).
- If an `In` has no `Out` and the next `In` is within reset window → leave incomplete pair, surface to supervisor.
- `FixedDollar`/`FixedHours` punches skip pairing — they go directly into shift enrichment.

### Stage 4 — Enrich pairs
- Compute `TotalHours` (already done).
- Attach the `Position` (and its rate) effective at `InPunch.Time`. If the employee has multiple positions, the punch carries one explicitly; if none specified, use the employee's default position.
- Cache the computed values rather than recomputing each access.

### Stage 5 — Build shifts
Existing logic mostly correct. Confirm:
- New `Shift` when gap > `PayRule.DistanceBetweenShiftsHours` (default 6).
- A `Shift` can hold pairs with different positions (multi-rate within one shift).

### Stage 6 — Date shifts
`PayRule.ShiftDateStrategy`:
- `FirstPunchLocalDate` — use the date of `InPunch.PunchTime` in the punch's local timezone (good for graveyard shifts)
- `MajorityHoursLocalDate` — assign to whichever date owns the most hours
This is per-PayRule because some clients have strong preferences.

### Stage 7 — Premiums (penalties)
Each `Shift` runs through every active `PremiumRule` (selected by jurisdiction). Each rule:
- `Applies(shift)` — yes/no
- `Calculate(shift, overrides)` — premium dollar amount + audit explanation
- Honors `SupervisorOverride` and/or `EmployeeWaiver` per rule's own rules. E.g. CA meal premium is waived only with **both** a supervisor approval **and** employee waiver; CO rest premium can't be waived at all.

State rules to implement:
- **California meal premium** — 1 hr at regular rate if no 30-min meal taken by 5th hour (and 2nd meal by 10th hour). Waivable in narrow cases.
- **California rest premium** — 1 hr if 10-min rest not provided per 4 hrs worked.
- **Colorado rest premium** — similar 10-min rule, paid time, no waiver.
- **Puerto Rico meal premium** — 1 hr at OT rate if meal not taken between 3rd and 6th hour.
- **Oregon meal premium** — 1 hr at regular rate for missed/short meal.
- **Washington meal premium** — paid 30 min in lieu of unpaid meal if missed.

Cap stacking: a shift can be hit by multiple premiums; default stack additively but per-rule logic decides. Make sure the explanation trail is preserved so a payroll auditor can see "why this $X premium was paid."

### Stage 8 — Time-based differentials
A `DifferentialRule` defines:
- Recurrence (RFC 5545 RRULE or equivalent) — daily/weekly/monthly windows, holiday calendar refs
- Window (e.g. 6pm-6am, or whole day of MLK day)
- Adjustment (flat $/hr added, multiplier on base, or fixed bonus)
- Qualification (min hours worked in the window to earn it)
- Stacking rule (does it combine with other diffs?)

For each pair, intersect its time range with active windows and apportion hours accordingly. Differentials affect the **regular rate** calculation downstream.

### Stage 9 — Group into Days
Bucket shifts by `ShiftDate`. Days are the unit for daily-OT and consecutive-day rules.

### Stage 10 — Group into Workweeks
`PayRule.WorkweekAnchorDay` (default Sunday 00:00 in employee's timezone) defines a 168-hr workweek per FLSA. Workweek is **independent** of pay period — bi-weekly periods contain exactly 2 workweeks; semi-monthly and monthly periods may straddle workweek boundaries (handle by computing OT on the workweek and prorating costs into periods).

### Stage 11 — Regular rate of pay (RROP)
Per workweek, per FLSA 29 CFR §778:
```
RROP = (Σ straight-time-earnings + non-discretionary bonuses + differentials) ÷ total hours worked
```
- Multi-rate: weighted average across positions.
- Non-discretionary bonus: spread across all hours in the period the bonus covers (single-week or retroactive multi-week recalc).
- Discretionary bonus: excluded from RROP.
- Differentials: included (they're additional compensation for work).
- Premiums (penalties): **excluded** — they're not for "hours worked," they're statutory damages.

### Stage 12 — Overtime
Driven by `PayRule.OvertimeRule`:
- Federal (default): >40 hrs/workweek at 1.5× RROP
- California: >8 hrs/day at 1.5×; >12 hrs/day at 2×; 7th consecutive workday — first 8 hrs at 1.5×, beyond 8 hrs at 2×
- Configurable so other states can be added (Alaska, Nevada, Colorado…)

**Output representation**: produce both views so the consumer chooses:
- Premium view: `straight = totalHours × RROP`, `overtimePremium = otHours × 0.5 × RROP` (and `× 1.0 × RROP` for double-time portion)
- Composed view: `regular = 40 × RROP`, `overtime = otHours × 1.5 × RROP`

Both come from the same underlying numbers; pick the view at serialization time.

### Stage 13 — Summarize
Build a `PayResult` per pay period with itemized lines: base, each differential, each premium, each bonus, OT premium, totals. Save as `PayCalculationSnapshot` with the rule version IDs used so it can be defended in audit.

---

## 5. Pay Rules & Effective Dating

The hard problem: an employee promoted Wednesday in the middle of a workweek. Their rate changes mid-week. FLSA still requires a single weighted RROP for the workweek.

### Approach
1. `EmployeePosition` is effective-dated: `(employee, position, rate, from, to)`.
2. `PayRuleAssignment` is effective-dated: `(employee, payRule, from, to)`.
3. When building pairs, **split** any pair that crosses an effective-date boundary at the boundary. Each resulting sub-pair carries the rate/rule active during its interval.
4. RROP and OT calc operate on these sub-pairs naturally — multi-rate weighted average already handles it.
5. If `PayRule` itself changes mid-week (e.g. employee moved from a fed-OT-only rule to a CA daily-OT rule), apply the rule that's active at the **end** of the workweek for workweek-level rules, but apply each day's rule to that day for daily-OT computation. Document this explicitly in code — it's a judgment call that auditors will ask about.

### Versioning
Every config entity (`PayRule`, `Position`, `DifferentialRule`, `PremiumRule`, `OvertimeRule`) has a `Version` and an `EffectiveFrom`/`EffectiveTo`. Edits create new versions, never mutate. `PayCalculationSnapshot` references the version actually used.

---

## 6. State-Specific Premium Rules

Use a registry pattern:
```csharp
public interface IPremiumRule {
    string Code { get; }              // "CA_MEAL", "CO_REST", etc.
    Jurisdiction Jurisdiction { get; }
    bool Applies(Shift shift, PremiumContext ctx);
    PremiumResult Calculate(Shift shift, PremiumContext ctx);
    OverrideAcceptance Accepts(OverrideKind kind);
}
```
`PayRule.ActivePremiumCodes` is a string set; the engine resolves codes to rule instances via DI. Adding a new state = implementing one class, registering it, opting it into the relevant `PayRule`s.

Override matrix (initial — verify with your legal source before shipping):
| Premium | Supervisor only | Employee only | Both required | Not waivable |
|---|---|---|---|---|
| CA meal | | | ✓ | |
| CA rest | | | | ✓ |
| CO rest | | | | ✓ |
| PR meal | ? | ? | ? | ? |
| OR meal | ? | ? | ? | ? |
| WA meal | ? | ? | ? | ? |

The `?` rows are open — these are the kind of things that need a state-by-state legal check before locking in.

---

## 7. Persistence — EF Core + PostgreSQL

### Will it scale?
**Yes**, comfortably to mid-six-figure employee counts and tens of millions of punches with standard hygiene. .NET 10 + Npgsql + Postgres 16 is a perfectly normal stack for this.

### Tactics
- **Decimal mapping**: configure `decimal(19,4)` for money, `decimal(10,4)` for hours.
- **Indexes**: `punches(employee_id, punch_time)` is the hot index. Add `pay_rule_assignments(employee_id, effective_from)`.
- **Partitioning**: partition `punches` by year (declarative partitioning in Postgres) once you cross a few million rows.
- **NodaTime**: use `Npgsql.NodaTime` — maps `Instant` to `timestamptz` natively.
- **AsNoTracking** for all read paths in the calculator; the calculator never persists via tracked entities.
- **Bulk inserts**: use `EFCore.BulkExtensions` or raw `COPY` for punch backfills.
- **Compiled queries** for the hot lookups (effective-dated rule resolution).
- **JSON columns** for the flexible bits: `DifferentialRule.RRule` text + `DifferentialRule.Metadata` jsonb — Postgres + EF Core JSON support is excellent.

### Caching
- `PayRule`/`Position`/`DifferentialRule` are read-mostly. Keep an in-memory cache (`IMemoryCache`) keyed by `(id, version)`. Invalidate on update.
- `StateMinimumWage` is reference data — cache for the lifetime of the process.

### Calculation as a job
- Calculation per `(employee, pay period)` is independent → trivial to parallelize. Run a worker queue (Hangfire, MassTransit, or a hosted service over Postgres SKIP LOCKED).
- Idempotent: re-running the same inputs produces the same `PayResult`. Snapshots are append-only.

### Multi-tenancy
If this serves multiple `Client`s in one DB, row-level filtering by `ClientId` everywhere is non-negotiable. Either EF global query filters or RLS in Postgres — both work; pick one and commit.

---

## 8. Scaling Notes & Risk Areas

| Concern | Mitigation |
|---|---|
| `decimal` perf is slower than `double` | Real-world overhead is negligible vs DB I/O. Don't pre-optimize. |
| Effective-dated lookups can N+1 | Compiled queries + caching; resolve all rules for a pay period in one query. |
| Calc CPU for retroactive bonus recalc | Process per-employee in parallel; cap concurrency to avoid DB connection storms. |
| Snapshot table growth | Partition `pay_calculation_snapshots` by year; archive old years to cheaper storage. |
| Premium rule explosion | Rule registry + per-`PayRule` opt-in keeps the active set small per calculation. |
| Daylight saving transitions | NodaTime handles correctly; tests must include DST edges. |
| Timezone bugs in shift dating | Centralize `ShiftDate` computation; property-based tests for boundaries. |

---

## 9. Things You Didn't Mention That Are Worth Considering

These are the ones most likely to bite you later if not designed in early. Not asking for decisions now — flagging so you can decide when each becomes relevant.

1. **Discretionary vs non-discretionary bonus distinction** — affects RROP. Tag `FixedDollar` punches with a `BonusKind`.
2. **Retroactive bonus recalculation** — FLSA requires it for non-discretionary bonuses that cover multiple workweeks. Snapshot model already supports recomputing; need a job to actually do it.
3. **Reporting-time pay (CA)** — minimum hours owed if sent home early. Treat as a premium rule.
4. **Spread-of-hours pay (NY)** — extra hour at min wage if shift spans 10+ hrs. Same — premium rule.
5. **Show-up / call-in pay** — minimum hours if reported on day off. Premium rule.
6. **Tipped employees** — tip credit reduces cash wage but RROP still uses full min wage. Out of scope per your call, but the model should allow `Position.IsTipped` to be added without schema upheaval.
7. **Comp time** — public-sector employers can offer time off in lieu of OT. Probably out of scope, but flag.
8. **Punch corrections vs originals** — your `PunchAudit` should capture original + every edit, never overwrite. Calculator should run against the latest-effective version of each punch.
9. **Idempotency tokens on ingest** — if a clock device retries a punch, you don't want duplicates. `(employee_id, device_id, device_punch_id)` unique constraint.
10. **Currency rounding strategy** — round only at presentation (line-item display), not between stages. Postgres `numeric(19,4)` preserves quarter-cents through the pipeline.
11. **Holiday calendar source** — federal holidays move (observed-Friday for Saturday holidays). Build this in once correctly.
12. **State changes mid-period** — employee moves CA→NV. Treat `Employee.State` as effective-dated too.
13. **Wage notice / pay-stub data** — many states (CA, NY) require the pay statement to itemize specific things. Your `PayResult` line items should already cover this if you're thorough.
14. **Approvals workflow** — at some point, a manager needs to approve timecards before payroll runs. Out of scope for the calculator but the data model should not preclude it.
15. **Test data factories + property-based tests** — for a calculation engine, hand-rolled examples will not cover the edge surface. Plan for `FsCheck` or similar early.

---

## 10. Phased Build Plan

Each phase is independently shippable and ends with passing tests for that scope.

### Phase 1 — Foundation (refactor what exists)
- Migrate `double` → `decimal` for all money/hour fields.
- Replace `PunchType` enum with `PunchKind` (`In`, `Out`, `FixedDollar`, `FixedHours`).
- Make calculator stages explicit single-responsibility classes; introduce `PipelineContext`.
- Lock in `WeightedOvertimeCalculator` semantics (returns premium only; documented).
- Add `PunchAudit` and treat all punch writes as immutable + audited.

### Phase 2 — Domain expansion
- `Position`, `EmployeePosition` (effective-dated), multi-rate enrichment in stage 4.
- `PayRule` v2: rounding, OT rule, shift-date strategy, reset window, workweek anchor.
- `RoundingRule` implementations + tests.
- Punch inference (stage 2) replacing manual type entry.

### Phase 3 — Pay rules & effective dating
- `PayRuleAssignment` effective-dated.
- Pair splitting at effective-date boundaries.
- `PayCalculationSnapshot` with rule-version references.

### Phase 4 — Time & date concerns
- NodaTime + `Npgsql.NodaTime` throughout.
- `TimeZoneId` on employee + punch.
- Shift dating strategies.
- Workweek grouping with configurable anchor.
- Pay period scaffold (weekly, bi-weekly, semi-monthly, monthly).

### Phase 5 — Differentials & bonuses
- `DifferentialRule` with recurrence + qualification.
- Bonus kinds (discretionary / non-discretionary).
- RROP including differentials + bonuses.
- Holiday calendar.

### Phase 6 — Overtime variants
- Daily OT, weekly OT, 7th-consecutive-day rule as composable `IOvertimeRule`s.
- Premium-view and composed-view output representations.

### Phase 7 — Premium rules
- `IPremiumRule` framework + override matrix.
- Implement CA meal, CA rest, CO rest, PR meal, OR meal, WA meal one at a time, each with thorough tests against published examples.

### Phase 8 — Persistence hardening
- EF Core configs, indexes, partitioning plan.
- Bulk-ingest path for punch backfills.
- Worker queue for parallel period calculations.
- Retroactive bonus recalculation job.

### Phase 9 — Test surface
- Property-based tests for stage purity & idempotency.
- DST edge cases.
- Effective-date-boundary edge cases.
- Multi-rate weighted-average edge cases.
- Recorded-fixture tests against your state-by-state expected outputs.

---

## 11. Open Decisions (to revisit before relevant phase)

| # | Question | Phase blocked |
|---|---|---|
| 1 | Override matrix for PR/OR/WA premiums | 7 |
| 2 | Workweek anchor default (Sunday 00:00 employee-local?) | 4 |
| 3 | Retroactive bonus job trigger (manual or auto on bonus insert?) | 8 |
| 4 | Holiday calendar source (hand-rolled vs `Nager.Date` library) | 5 |
| 5 | Multi-tenancy model (EF query filters vs Postgres RLS) | 8 |
| 6 | Worker queue choice (Hangfire / MassTransit / hand-rolled SKIP LOCKED) | 8 |
| 7 | Idempotency token shape for ingest (`device_id + device_punch_id`?) | 1–2 |
| 8 | Per-state minimum-wage data source (manual seed vs API) | 5 |
