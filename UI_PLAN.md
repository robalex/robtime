# RobTimeUI — Frontend & API Plan

Companion to `PLAN.md` (the engine plan). That document declared "HTTP API, UI" out of scope; this
one brings them into scope. Read `CLAUDE.md` first for the engine architecture.

---

## 1. Where things actually stand

Worth being blunt about this, because it reframes the effort: **most of the work below is backend,
not React.** The UI is the easy half.

`TimeCalculation.Api` today is four `POST` endpoints — `/clients`, `/employees`, `/payrules`,
`/punches` — with no reads, no updates, no deletes, no auth, no paging, and no list endpoints. A
configuration UI is ~80% reads. There is also nothing to log in as.

### Gaps that must close before (or alongside) the UI

| # | Gap | Why it blocks the UI |
|---|---|---|
| **A** | **No user concept at all.** `CreatedBy` is a client-supplied string on the request body. | You asked for users and admins. Nothing to build on. |
| **B** | **No read endpoints.** Four POSTs, zero GETs. | Every screen in a config app is a list or a detail view. |
| **C** | **Endpoints return EF entities** (`Created<Client>`, `Created<PayRule>`). | Generated TS types would track internal model shape and drift on every refactor. Needs response DTOs. |
| **D** | **`DifferentialRule` is not persisted** — no `DbSet`, no `ClientId`, no association to `PayRule`. `PipelineContext` takes them as a free-floating list. | You asked to edit "all of the pay rules we have specified." Differentials are half of that and there is nowhere to store them. |
| **E** | **No per-employee rate.** `EmployeePositionAssignment` carries only a `Position`, whose `BaseRate` is client-wide. | `PLAN.md` §3 specified `EmployeePosition` with its own `Rate`. Today, paying two people differently for the same job means duplicating the Position. This will be the single most confusing thing in the employee editor. |
| **F** | **`PayRule` versioning is nominal.** `Version` is an int nobody increments; there is no `EffectiveFrom`/`EffectiveTo` on the rule itself. An edit mutates the row. | `PLAN.md` §5 says "Edits create new versions, never mutate." Right now, editing a pay rule silently rewrites the past. **A UI makes editing easy, and therefore makes accidental retroactive rewrites easy.** See §7 — this also makes impact-preview impossible to retrofit. |
| **G** | **`HolidayCalendar` is code-only** (`UsFederal(year)`), not stored, not per-client. | Differentials reference holidays; clients have their own. |
| **H** | **`PayRule` has no `Name`/`Description`.** | A list showing "PayRule 3, PayRule 7" is unusable. |
| **I** | **Premium overrides/waivers aren't persisted** (`OverrideKind`/`WaiverPolicy` are model types with no table). | Needed for the supervisor override screen. Not urgent — Phase 6+. |
| **J** | **Multi-tenancy is wired but inert.** `PayrollDbContext` accepts `tenantClientId`, but `AddDbContext` never supplies one, so every filter short-circuits to "no filter." | Auth is what turns it on. Until then, any caller sees every client's data. |

Gaps **E** and **F** are model changes. They are cheap now and expensive after there's a UI and
production data shaped around the current behaviour. Do them in Phase 0.

---

## 2. Stack — decisions, not options

Chosen and justified; tell me to swap any of them.

| Concern | Choice | Why this one |
|---|---|---|
| Build/dev | **Vite 7 + React 19 + TypeScript 5.9** (strict) | Uncontested default. No SSR need — this is an authenticated internal tool. |
| Routing | **TanStack Router** | File-based *and* fully type-safe params/search. Search-param typing matters here: list filters (`?clientId=3&effectiveOn=2026-07-01`) should be URL state, and this is the only router that types them. |
| Server state | **TanStack Query v5** | Caching/invalidation for a read-heavy config app. Effective-dated data invalidates in fan patterns (edit an assignment → invalidate the employee, the timeline, the impact preview) — query keys handle this cleanly. |
| Forms | **React Hook Form + Zod v4** | Pay-rule forms are 20+ fields with cross-field rules (grace ≤ interval/2, daily-OT fields only meaningful when `HasDailyOvertime`). Uncontrolled + schema resolver is the right shape. |
| UI kit | **shadcn/ui on Tailwind v4** | Source-in-repo, so the effective-dated timeline and diff components can be built from the same primitives instead of fighting a component library. |
| Tables | **TanStack Table v8** (headless, shadcn-styled) | Employee lists need sort/filter/paginate against server-side paging. |
| Dates | **`@js-joda/core` + `@js-joda/timezone`** | See §4 — this is a deliberate pairing, not a default. |
| Testing | Vitest + Testing Library, **MSW** for API mocking, Playwright for auth/CRUD smoke | MSW handlers generate from the same OpenAPI doc, so mocks can't drift from the contract. |

**Repo layout: `RobTime/RobTimeUI/`, in this repo.** Not a separate repo. The contract pipeline
(§3) is a build-time file dependency from the API project to the UI project; across two repos that
becomes a publish-and-consume versioning problem for zero benefit at this scale.

```
RobTime/
  TimeCalculation.Api/          # emits openapi/v1.json on build
  RobTimeUI/
    src/
      api/            schema.d.ts (generated — do not edit), client.ts, queries/
      components/     ui/ (shadcn), effective-dated/, forms/
      features/       clients/ employees/ positions/ pay-rules/ differentials/ users/
      routes/         TanStack Router file routes
      lib/            dates.ts, money.ts, permissions.ts
```

---

## 3. The contract pipeline (.NET → TypeScript)

You've done dotnet-generates-TypeScript before. Current best practice has moved off NSwag's
"generate a whole client class" model toward **generate types only, use a tiny typed fetch wrapper**.

```
dotnet build                      →  TimeCalculation.Api/openapi/v1.json
npm run gen:api                   →  src/api/schema.d.ts        (openapi-typescript)
openapi-fetch createClient<paths> →  fully typed paths/params/bodies/responses
```

**Setup:**

1. Add `Microsoft.Extensions.ApiDescription.Server` to `TimeCalculation.Api.csproj`, with
   `<OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>` and
   `<OpenApiDocumentsDirectory>openapi</OpenApiDocumentsDirectory>`. `AddOpenApi()` is already
   registered in `Program.cs`, so this is additive. (Verify exact property names against the .NET 10
   docs — they shifted between 8 and 9.)
2. UI `package.json`: `"gen:api": "openapi-typescript ../TimeCalculation.Api/openapi/v1.json -o src/api/schema.d.ts"`.
3. **Commit `schema.d.ts`.** A CI step regenerates and fails on diff — that turns "someone changed
   the API and broke the UI" into a red build on the API PR, not a runtime 400 next week.

**Why this over the alternatives:** NSwag/Kiota generate a client class per endpoint group — more
code, more coupling, and a runtime dependency you maintain. `openapi-typescript` emits *only*
types (zero runtime), and `openapi-fetch` is ~2 kB that makes `client.GET("/employees", { params })`
fully typed including the response union. `@hey-api/openapi-ts` is the other credible modern option
(it generates TanStack Query hooks too) — reasonable if you'd rather have hooks generated than
hand-write the ~30 query hooks. I'd start with `openapi-fetch`; the hooks are thin and being able
to read them matters more than saving the keystrokes.

**Zod stays hand-written.** Generated types are the source of truth for *shape*; Zod schemas are
for *form validation*, where the messages are UX copy, not contract ("Grace minutes must be at most
half the rounding interval" — the rule the API already enforces in `PayRuleEndpoints`, mirrored
client-side for instant feedback). Keep them honest with `satisfies` against the generated type.

### One thing that needs API work first

`PayRule.ActivePremiumCodes` is a bare `string[]` over the wire. The UI cannot render a checkbox
list from that without hardcoding `"CA_MEAL"`. Add:

```
GET /metadata/premium-rules  →  [{ code, name, jurisdiction, waiverPolicy, description }]
```

sourced from the engine's `PremiumRegistry`. Note this makes the API project reference
`TimeCalculation` (the engine) for the first time — its `.csproj` comment explicitly anticipates
this: *"Add a reference to the TimeCalculation engine project only when an endpoint needs to
actually invoke PayCalculator."* Same reference unlocks §7.

---

## 4. NodaTime over the wire

Worth its own section because it's where this kind of app usually rots.

The API already registers `ConfigureForNodaTime` + `JsonStringEnumConverter`, so:

| .NET | JSON | TS (generated) | Frontend type |
|---|---|---|---|
| `Instant` | `"2026-07-22T14:30:00Z"` | `string` | `js-joda` `Instant` |
| `LocalDate` | `"2026-07-22"` | `string` | `js-joda` `LocalDate` |
| `LocalTime` | `"18:00:00"` | `string` | `js-joda` `LocalTime` |
| `IsoDayOfWeek` | `"Monday"` | `"Monday" \| ...` (free) | enum union |

Everything time-shaped arrives as an untyped `string`. **Use `@js-joda/core`** — it is the direct
port of the same Joda-Time lineage NodaTime came from, so `LocalDate`/`LocalTime`/`Instant`/
`ZoneId` map one-to-one with the C# types. A `DifferentialRule` window of 18:00–06:00 is a
`LocalTime` pair with wraparound semantics on both sides, and `Employee.HomeTimeZoneId` is a tzdb
id both libraries resolve identically. Using `Date` or `dayjs` here means re-deriving those
semantics by hand and getting the midnight-wrap and DST cases wrong — which the engine's own test
suite exists specifically to prevent.

Wrap the boundary in `lib/dates.ts`: parse on the way in, format on the way out, and never let a
raw date string reach a component. Brand the generated strings (`type LocalDateString = string &
{__brand: 'LocalDate'}`) so a bare string can't be passed where a date is expected.

**Money:** `decimal(19,4)` in Postgres → `number` in JS. Rates like `18.7350` are fine in a double,
but do **no arithmetic** client-side beyond display. Totals come from the server. `PLAN.md` §7 is
explicit that rounding happens at presentation only; the frontend is presentation, so it formats
and never sums.

---

## 5. Users, roles, tenancy, and data protection

### Model

New project `TimeCalculation.Identity` with its own `AppIdentityDbContext` (ASP.NET Core Identity +
EF), same database, separate migrations history table. **Not** folded into `PayrollDbContext` —
that project deliberately depends only on `TimeCalculation.Model`, and dragging
`Microsoft.AspNetCore.Identity.EntityFrameworkCore` into it would break the layering discipline
`CLAUDE.md` and the Persistence README both go out of their way to establish.

```csharp
class AppUser : IdentityUser<int>
{
    public int? ClientId { get; set; }     // null only for SystemAdmin
    public int? EmployeeId { get; set; }   // set when this user IS an employee
    public string DisplayName { get; set; }
}
```

### Roles

| Role | Can |
|---|---|
| `SystemAdmin` | Everything, across all clients. Creates clients and client admins. Rare. |
| `ClientAdmin` | Everything within one client: employees, positions, pay rules, differentials, users. |
| `Supervisor` | View/edit punches for their client, approve premium overrides. Read-only on config. |
| `Employee` | Own punches, own profile. Read-only on everything else. |

This is the smallest set that covers what you described (employee self-service + at least one
admin) while leaving room for the supervisor override workflow `PLAN.md` §6 already models. Start
with role-based checks behind a `lib/permissions.ts` shim so moving to claims/policies later is a
one-file change.

### Login: email + password

Everyone — employees included — signs in with email and password for now.
`MapIdentityApi<AppUser>()` gives login/logout/refresh/password-reset/2FA endpoints out of the box.

> **TODO — timeclock authentication.** Employees on a shop floor will not log in with email and
> password on a shared wall-mounted clock. Later we want a **timeclock concept**: a registered
> device, plus an **employee badge number** the employee enters (or swipes) *only at a clock*. The
> badge number is a clock-only credential — it must never be accepted by the web app, and it does
> not replace the employee's own login. The data model already anticipates the device half of this:
> `Punch.DeviceId` / `Punch.DevicePunchId` exist with a unique index for idempotent ingest
> (`PLAN.md` §9 item 9). What's missing is a `Device` registration table, `Employee.BadgeNumber`
> (unique per client), and a separate auth scheme for device endpoints. See §11.

### Auth mechanism: cookies for now

Cookie auth: `HttpOnly`, `SameSite=Strict`, `Secure`. Same-origin in production (API serves the
built SPA), Vite proxy in dev so it's same-origin there too.

Rationale: bearer tokens in `localStorage` are readable by any XSS; an HttpOnly cookie is not.

> **Why we will likely outgrow this.** Cookies are right for a same-origin browser admin UI and
> wrong for almost everything else, and three things on the roadmap are "everything else":
>
> 1. **Timeclock devices** (the TODO above). A wall clock or native app cannot participate in
>    `SameSite` cookie flows. It needs a bearer token or mTLS.
> 2. **Enterprise SSO.** A true SaaS product sells to companies that require SAML/OIDC sign-in.
>    That flow terminates in tokens.
> 3. **A public/partner API.** Payroll exports and HRIS integrations authenticate with API keys or
>    bearer tokens, not session cookies.
>
> The mitigation is cheap and should be honoured from day one: **ASP.NET Core supports multiple
> authentication schemes side by side.** Adding a JWT bearer scheme for `/api/device/*` or
> `/api/public/*` later is purely additive as long as authorization is written against *policies and
> claims*, never against "is there a cookie." Concretely — no endpoint should ever read the cookie
> directly, and `lib/permissions.ts` on the frontend should key off the user object returned by
> `/me`, not off cookie presence.

### Multi-tenant isolation (SaaS)

Since this is true multi-tenant SaaS, isolation is a correctness requirement, not a feature. Two
things are true at once: the current implementation has a real hole, and your performance instinct
about global filters is right — but not for the reason usually given.

**Use global query filters, not per-query `.Where()`.** The failure modes are asymmetric. Forgetting
a `.Where(x => x.ClientId == tenantId)` on one query out of three hundred is a silent cross-tenant
data leak — the single worst bug class in a SaaS product, and nothing about it is loud. Global
filters make the safe path automatic and the unsafe path explicit: escaping requires
`IgnoreQueryFilters()`, which is one grep in code review and can be banned outright by a Roslyn
analyzer outside a small allow-listed admin namespace.

**The performance problem is not global filters — it's this specific filter.** Every filter in
`PayrollDbContext` is shaped:

```csharp
b.HasQueryFilter(e => _tenantClientId == null || e.ClientId == _tenantClientId);
```

That emits `WHERE (@tenant IS NULL) OR (client_id = @tenant)`. Postgres can fold that away when it
builds a *custom* plan (parameters substituted, `NULL IS NULL` collapses the OR), but once it
switches to a *generic* plan — which it does after a few executions of a prepared statement — it
cannot, and you lose clean index access on `client_id`. The result is a query that is fast in
testing and intermittently slow in production, which is the worst possible failure shape.

Fix: **make the tenant id required and drop the null escape hatch.** The filter becomes a plain
`WHERE client_id = @tenant` — fully sargable, indexes normally, no plan variability. The
"no tenant" case (`SystemAdmin`, background jobs, migrations) is served by explicitly constructing a
context without the filter, not by a runtime null check compiled into every query in the system.

**Then the real lever is indexing, and here is the gap.** Confirmed against the current model:

| Table | Tenant filter today | Problem |
|---|---|---|
| `clients`, `employees`, `positions` | Yes | Only the `== null ||` shape above. |
| `pay_rules` | Yes | Same. |
| `punches` | **None** — only `!p.IsDeleted` | **Not tenant-scoped at all.** `Punch` has `EmployeeId` but no `ClientId`. |
| `punch_audits` | **None** | Same. |
| `pay_rule_assignments` | **None** | Reachable only via `EmployeeId`. |

Punches are the hottest and largest table in the system and they are currently readable across
tenants. Filtering them through a navigation (`p => p.Employee.ClientId == tenant`) would close the
hole but force a join onto every punch query forever.

**So: denormalize `ClientId` onto `punches`, `punch_audits`, and both assignment tables**, and make
it the *leading* column of their indexes — `punches(client_id, employee_id, punch_time)` replacing
today's `(employee_id, punch_time)`. A tenant-scoped index prefix is what makes multi-tenant queries
scale; it also sets up the partitioning `PLAN.md` §7 anticipates. Denormalized `ClientId` is
immutable in practice (an employee does not change employer within a tenant), so the usual
denormalization objection doesn't apply.

EF Core 9 (the version referenced) allows **one** query filter per entity type, so `punches` must
combine both predicates into a single expression: `p => p.ClientId == _tenantId && !p.IsDeleted`.
EF Core 10's named filters would let these be declared separately if you upgrade.

Postgres RLS stays open as defense-in-depth (`PLAN.md` open decision #5) — but it is a second lock
on the same door, not a substitute. Land EF filters plus the isolation test suite in Phase 1; revisit
RLS when a compliance review asks for it.

### What auth unlocks

- Resolve `_tenantClientId` from the principal's `ClientId` claim when constructing
  `PayrollDbContext` — **this activates the multi-tenant filters that are currently dead code (Gap J).**
- Delete `CreatedBy` from every request contract; take it from the authenticated user. Right now any
  caller can claim to be anyone, which makes `PunchAuditEntry` decorative. (`PunchAuditEntry` already
  has an `ActorUserId` field waiting for exactly this.)

---

### Data protection & encryption at rest

*Not legal advice — the compliance specifics below need a lawyer's read before you rely on them,
particularly the California points. The engineering analysis stands on its own.*

**Short answer: no column-level encryption today, and the reason is more useful than the answer.**
There is a specific tripwire that flips this, and the cheap thing to do now is make sure crossing it
later is a small change rather than a migration under pressure.

#### What's actually stored

| Data | Where | Sensitivity |
|---|---|---|
| Name, salutation, post-nominals | `Employee` | Low–moderate |
| Email, password hash | `AppUser` (Phase 1) | Hash is handled by Identity — never store or log the password |
| Wage rate | `Position.BaseRate`, `EmployeePositionAssignment.Rate` | Moderate |
| Hours worked, punch times | `Punch` | Moderate — this is behavioural data (when someone comes and goes) |
| Actual earnings | `PayResult` / `PayCalculationSnapshot` | Moderate–high |
| **SSN / tax ID, bank details, DOB, home address** | **Nowhere — none of these exist yet** | **High** |

That last row is the whole answer. Everything currently in the model is *employment* data. The
high-risk identifiers are all in `PLAN.md`'s out-of-scope list (payroll tax, W-2/1099, direct
deposit) — and every one of them arrives the day you implement those.

#### The three things "encryption at rest" can mean

1. **Volume / storage encryption** — RDS, Azure Database for PostgreSQL, and Cloud SQL all do this
   with a checkbox. Protects against a stolen or improperly decommissioned physical disk.
2. **Cluster-level TDE** — Postgres has no native TDE in core; the managed offerings' storage
   encryption is the practical equivalent. Same threat model as (1).
3. **Column-level / application-side encryption** — the app encrypts before writing, decrypts after
   reading. The database sees ciphertext.

**Turn on 1 and 2 immediately.** They're free, they're one line of Terraform, and every security
questionnaire you will ever fill out asks about them.

But be clear-eyed about what they buy: **they defend against physical disk theft, which is not how
breaches happen.** If an attacker obtains a valid connection — stolen credentials, SQL injection, a
compromised app server, a leaked `pg_dump` — TDE decrypts everything for them transparently. Layers
1 and 2 are compliance-checkbox value with near-zero real-world protection against the actual threat
model. Only layer 3 helps, and layer 3 is genuinely expensive.

#### Why column encryption would be actively harmful here

This isn't abstract caution — for *this* application the costs are severe and specific:

- **No aggregation in SQL.** This is the killer. Encrypt `PayLineItem.Amount` and `SUM()`,
  `WHERE amount > x`, and `ORDER BY amount` all become impossible. Every total has to be pulled into
  the app, decrypted, and summed in C#. The §7 impact-preview feature aggregates and diffs
  `PayResult`s across thousands of employees — encrypting pay amounts would make the feature you most
  want either unbuildable or unusably slow.
- **No indexing worth having.** Randomized encryption can't be indexed at all. Deterministic
  encryption can, but leaks equality — an attacker sees which employees share a wage, and can
  frequency-analyze from there. You give up security to get back a fraction of the query model.
- **Server-side paging dies.** Phase 3's employee list sorts by last name. Encrypt it and you must
  load every row and sort in memory — which defeats the paging the list exists to do.
- **EF Core has no first-class support.** You'd use a `ValueConverter`, which works for read/write
  but silently pushes filtering and sorting to client evaluation, and breaks the deliberate
  `HasPrecision(19,4)` money mapping (ciphertext is `bytea`).
- **You trade a data problem for a key problem.** Rotation across millions of rows, escrow, HSM/KMS
  integration — and if the key is lost the data is unrecoverable. Per-tenant keys are the right shape
  for SaaS (one tenant's key compromise doesn't expose the others, and it enables crypto-shredding
  on deletion) but they multiply the operational surface.

#### Where the liability actually attaches — and why the tiering lines up

The practical driver isn't a statute that says "encrypt payroll data" — none does. It's the
**encryption safe harbor** in state breach-notification law: breached data that was encrypted, with
the key uncompromised, generally doesn't trigger notification.

The nuance that decides this design: those statutes define the covered data narrowly. California's
private right of action for breaches (Civ. Code §1798.150, damages of **$100–$750 per person per
incident** without proof of harm) attaches to *nonencrypted* **name + SSN / driver's licence /
financial account number / medical information**. Name plus wage is not in that set.

Two consequences, and they point the same way:

- **Encrypting names and wages buys liability protection you don't currently need**, at the cost of
  the query model you very much do.
- **Encrypting SSN and bank details buys the safe harbour exactly where the liability is.** So
  encrypt precisely the Tier A set, when it arrives, and nothing else.

Also note: California's HR-data exemption expired 1 Jan 2023, so employee data in a CPRA-covered
client is in scope. With a 5,000-employee client, the statutory range above is a $3.75M exposure on
a single incident. New York's SHIELD Act imposes a similar "reasonable safeguards" duty covering
employees. And in practice **SOC 2 Type II will force the conversation long before any regulator
does** — expect it from your second or third enterprise customer.

#### The tripwire

> **The day an SSN, tax ID, bank account, or date of birth enters this system, column-level
> encryption becomes mandatory for those fields.** That day is when you build W-2s or direct
> deposit — both already on the roadmap as out-of-scope-for-now.

#### What to do now (all cheap, all preserve the option)

1. **Storage encryption + TLS in transit.** `SSL Mode=Require` on the Npgsql connection string.
2. **Encrypt backups under a separate key from the volume.** Backups are the most common real-world
   leak vector, and this is the one place layer-1/2 encryption genuinely earns its keep.
3. **Reserve a separate table for Tier A data — the single most valuable decision here.** When SSN
   arrives, it must land in `employee_sensitive` (1:1 FK to `employees`), not as a column on
   `employees`. Then column encryption applies only to a table that is read rarely, by few code
   paths, and never by the calculation engine — instead of to a table every screen queries. Adding
   `employees.ssn` later means either encrypting a hot table or migrating under time pressure.
4. **Keep PII out of the calculation and audit path — you already have this, so protect it.**
   I checked: the entire engine reads exactly three `Employee` fields — `Id`, `MinimumWage`, and
   `HomeTimeZoneId`. It never touches names. And `PayCalculationSnapshot` stores `EmployeeId`, not a
   name. Since snapshots are append-only and retained for years for audit, that's a large PII
   retention surface you don't have. **Don't let a "convenient" denormalised `EmployeeName` get added
   to a snapshot or line item.** Worth a comment on the type.
5. **Field-level authorization beats encryption for the insider threat**, which is the realistic risk
   in payroll. Concretely: *should a `Supervisor` see wage rates at all?* They need to approve
   punches and premium overrides — neither requires knowing what anyone earns. I'd say no, and that
   belongs in the §5 role matrix now rather than as a retrofit.
6. **Read auditing on sensitive data.** Who looked at whose pay, and when. For insider misuse this is
   worth more than any encryption scheme, because encryption doesn't protect against a user with
   legitimate credentials.
7. **PII in logs is the leak nobody plans for.** Structured logging with redaction; never log request
   or response bodies for employee/pay endpoints. Cheap now, painful to retrofit once log volume and
   retention are established.
8. **Per-tenant key derivation, decided now even if unused.** If Tier A encryption is per-tenant from
   the start, "delete this client's data" becomes "destroy this client's key" — which answers both
   contract termination and CCPA deletion requests cleanly.

---

## 6. Information architecture — the anti-overwhelm design

Your instinct is right: this app has ~40 configurable fields across 7 entity types, and dumping
them in a nav tree is how config tools become unusable. Four rules, applied consistently:

### Rule 1 — Four top-level destinations, no more

```
Dashboard  │  People  │  Time  │  Setup
```

`People` lands on the employee list (the most-visited screen in any T&A system). `Setup` lands on a
**card grid, not a menu** — each card is a config area with a one-line description, a count, and a
"3 need attention" badge. Cards are scannable and self-describing; nested menus require you to
already know where things are.

### Rule 2 — Three-tier field taxonomy in every editor

Essential (always visible) → Common (visible, grouped) → Advanced (collapsed accordion).
Applied to `PayRule`, which is the worst offender:

| Tier | Fields |
|---|---|
| **Essential** | Name, Workweek start day, Weekly OT threshold |
| **Common** | Daily OT toggle + thresholds, 7th-day rule, Rounding strategy/interval/grace |
| **Advanced** | `PunchPairResetHours`, `MaxShiftLengthHours`, `DistanceBetweenShiftsHours`, `ExpectedBreak/LunchLengthMinutes`, `ShiftDateStrategy`, `ActivePremiumCodes` |

Every engine default already lives in `PayRule`'s property initializers, and
`CreatePayRuleRequest`'s doc comment is emphatic about not duplicating them. The UI honours the
same discipline: **show the default as placeholder text, send `null`, let the server decide.**

### Rule 3 — Templates before fields (mandatory start, fully editable after)

Creating a pay rule **must** start by picking a template — *Federal Standard*, *California*,
*Colorado*, *Oregon*, *Washington*, *Puerto Rico* — which presets everything including
`ActivePremiumCodes` and the daily-OT/7th-day flags. There is no blank-slate option.

**After that, every field is editable**, including the ones the template set. The template is a
starting point, not a constraint.

This inverts the problem: instead of "here are 20 knobs, good luck," it's "you're on California
rules, and you've changed 2 things." It also encodes the state-by-state knowledge that currently
only exists in `PLAN.md` §6 and the premium rule classes.

Because everything stays editable, the **template lineage has to be tracked deliberately** or it
becomes meaningless the moment someone edits a field:

- `PayRule` stores `TemplateCode` and `TemplateVersion` — what it was derived from.
- Every field that differs from its template value renders a "modified" dot with a one-click revert,
  and the editor header summarises: *"California — 3 settings customised."*
- The Advanced accordion auto-expands if it contains any modified field, so customisations can never
  hide behind a collapsed section.
- When a template itself is updated (a state changes its OT threshold), rules derived from it are
  flagged for review with a diff — **never auto-migrated.** Silently changing a client's pay rules
  is exactly the retroactive-rewrite failure Gap F is about.

### Rule 4 — One effective-dated widget, everywhere

`EmployeePositionAssignment` and `PayRuleAssignment` are the same shape: `(thing, from, to?)`.
`Employee.State` should join them (`PLAN.md` §9 item 12), and so should the per-employee rate once
Gap E closes. Build **one** `<EffectiveDatedTimeline>` component — a horizontal band of periods
with a "Change effective…" action — and reuse it. Users learn effective dating once.

Corollary: **effective-dated changes never happen in a modal.** They get their own route, so the
effective date is prominent and the URL is linkable ("here's the change I'm proposing"). Modals
encourage skimming past the date field, and the date field is the whole point.

### Screen inventory

| Area | Screens |
|---|---|
| People | Employee list · Employee detail (tabs: Details / Positions & Rates / Pay Rule / Punches) · Position list + editor |
| Setup | Setup home (cards) · Client list + editor · Pay rule list + editor + template picker · Differential list + editor · Premium selection · Holiday calendars · Users & roles |
| Time | Punch entry (self-service) · Timecard view *(Phase 6)* |

---

## 7. The hard one: showing the effect of a pay rule change

You flagged this as the most complicated part. It is, but the engine has already done the load-bearing
work, and the reason it has is worth stating: `PayCalculator.Calculate` is **pure and deterministic**
— `CLAUDE.md` says so and `PropertyBasedTests` asserts it over seeded random inputs. That means
impact preview is not a new algorithm. It's *run it twice and diff*.

```
current PayRule  ─┐
                  ├─→ PayCalculator ─→ PayResult ─┐
punches, period  ─┤                                ├─→ diff → per-employee, per-shift, per-line-item
                  ├─→ PayCalculator ─→ PayResult ─┘
draft PayRule    ─┘
```

`PayResult` → `WorkweekPay` → `ShiftPay` → `PayLineItem` is already drillable, and `PayLineItem`
carries `ShiftDate`/`AnchorPunchId` for identity — so line items from two runs can be *matched*, not
just totalled. That's the difference between "total changed by $1,204" and "Jane's Tuesday shift
gained a CA meal premium because rounding pushed her lunch past the 5th hour." The second one is
what makes the feature worth building.

### Two-step delivery

**Phase 4 down payment — single-employee what-if.** In the pay rule editor: pick one employee, pick
one past week, run both configs synchronously, show a side-by-side line-item diff. No queue, no job
table, no new infrastructure — one endpoint, and the engine reference §3 already requires. This
gets ~80% of the confidence for ~5% of the work, and it lets the diff UI get designed and tested
before anything has to scale.

**Phase 7 — full impact preview.** `POST /payrules/{id}/impact-preview` over a whole client and a
whole period is thousands of independent calculations. `PLAN.md` §7 already notes this is
"trivial to parallelize" and open decision #6 (worker queue) is exactly this. So: POST returns a job
id, poll for the result, render a summary ("12 of 84 employees affected, +$1,204.50; largest: Jane
Doe +$310") that drills into the per-employee and per-shift diffs from Phase 4.

### The prerequisite — and why it's Phase 0, not Phase 7

**None of this works if editing a pay rule mutates the row (Gap F).** "Current vs. draft" is only
expressible if a rule has versions and effective dates of its own. Fix it in Phase 0:

- `PayRule` gets `EffectiveFrom` / `EffectiveTo` / `Status` (`Draft` | `Active` | `Superseded`) and
  a real `Version` that increments.
- Saving an edit to an `Active` rule creates a new version; it does not `UPDATE` in place.
- `PayCalculationSnapshot` (already designed to reference "the rule versions used") becomes truthful.

This is the one item where deferring is genuinely expensive: retrofitting versioning after the UI
ships means migrating production config data whose history was already destroyed by in-place edits.

---

## 8. Phases

Each phase is independently shippable. Phases 0–1 have no UI deliverable — that's the honest cost
of the current API surface.

### Phase 0a — Prerequisites *(do these first; they're small)*

Baseline verified 2026-07-22: `dotnet build` clean, **281/281 tests pass**, and
`PERF_FIXES_PLAN.md` is functionally complete (1.1–1.4 and 2.1–2.3 all landed; 2.4 and 2.5 were
deliberately skipped with reasons recorded). Nothing is half-finished. What's left:

- [x] **Package upgrade: 9.0.4 → 10.x — done 2026-07-22.** Bumped every EF Core / ASP.NET Core /
      Npgsql package to the `10.x` line matching `TargetFramework net10.0`, plus the small
      same-graph NodaTime/serializer patch bumps. Confirmed after the bump:
      - The OpenAPI document is now generated as **3.1.1** (verified by booting the API and
        fetching `/openapi/v1.json` — was 3.0 before). This is the version the TypeScript codegen
        pipeline in §3 will read from, so it's now locked in before any UI code depends on it.
      - `Microsoft.AspNetCore.OpenApi` 10.0.10's source generator needs `Microsoft.OpenApi` on its
        **2.x** API surface (`IOpenApiMediaType.Example` is a settable property there; 3.x made it
        read-only and the generator fails to compile against it) — pinned the transitive
        `Microsoft.OpenApi` dependency to **2.11.0** explicitly, the newest 2.x release, which also
        clears the NU1903 high-severity advisory (GHSA-v5pm-xwqc-g5wc) that 2.0.0 carried. Do not
        bump `Microsoft.OpenApi` past 2.x until `Microsoft.AspNetCore.OpenApi`'s generator does.
      - EF Core 10 obsoletes `IReadOnlyEntityType.GetQueryFilter()` in favor of
        `GetDeclaredQueryFilters()` (the named-filters API referenced in §5) — updated the two
        `PersistenceModelTests` call sites; no behavior change, just the new accessor.
      - Also fixed the pre-existing xUnit2031 warning in `StatePremiumEndToEndTests.cs:65` while in
        there, since it was already on the punch list below. Build is now **0 warnings, 0 errors**.
      - Full solution rebuild (`--no-incremental`) and `dotnet test`: still **281/281 passing**. App
        boots and serves `/openapi/v1.json` successfully on the new packages.
      - Changed files: the four `.csproj`s, `PersistenceModelTests.cs`,
        `StatePremiumEndToEndTests.cs`. Committed (`e90e0b6`) and pushed to `origin/main`.
- [x] **Set up CI — done 2026-07-22 (`.github/workflows/ci.yml`).** Build (Release,
      `TreatWarningsAsErrors=true`) + test on push/PR to `main`, `.trx` results uploaded as an
      artifact. Runs on `ubuntu-latest` with no Postgres service container — confirmed the only
      test touching `PayrollDbContext` is `PersistenceModelTests`, which builds the EF model
      against the Npgsql provider without connecting (per the Persistence README), so the full
      281-test suite is genuinely DB-free. Verified all three CI steps locally in Release config
      before committing the workflow. Not yet exercised by an actual PR — first PR against `main`
      will be the real test of the YAML.
- [x] **Confirm database state — done 2026-07-22.** Local Postgres reachable on `localhost:5432`;
      `dotnet ef migrations list` / `dbcontext info` confirm `Initial` is applied to the `robtime`
      database and no migrations are pending. Also updated the global `dotnet-ef` tool 9.0.7 →
      10.0.10 to match the just-upgraded runtime — it was silently working against a version
      mismatch before.
- [x] **Close out `PERF_FIXES_PLAN.md` — done 2026-07-22.** Traced each item to its landing commit
      via `git log`/`git show`: Part 1 (1.1–1.4) in `da41334`, Part 2 (2.1–2.3) in `2920a0b`. Marked
      every heading with its commit hash; added a closure banner at the top so the file reads as
      history, not a live checklist, for anyone picking this up cold.
- [ ] **Answer §10 Q1 and Q4** — they block Phase 2 nav and Phase 3 screens respectively. Needs you.
- [x] **Legal review of premium waiver policies — deferred, not scheduled.** Decided 2026-07-22:
      don't chase this ahead of need. PR/OR/WA templates simply aren't offered until a client in
      one of those states is actually being onboarded — see the revised note in Phase 4. Revisit
      `PLAN.md` open decision #1 at that point, not before.
- [x] **`.gitignore` — done 2026-07-22.** Added `RobTimeUI/{node_modules,dist,.vite,*.local}`
      ahead of the folder existing.

### Phase 0 — API foundation *(backend only)*
- Response DTOs for every entity; stop returning EF entities (Gap C).
- Full CRUD: `GET` list (paged/sorted/filtered), `GET` by id, `PUT`, soft-delete — Client, Employee,
  Position, PayRule (Gap B).
- Model changes: `PayRule.Name`/`Description` (H), `EmployeePositionAssignment.Rate` (E),
  **`PayRule` versioning + effective dating + draft status (F)**, `PayRule.TemplateCode`/`TemplateVersion`.
- **Tenancy schema prep** (§5): add `ClientId` to `Punch`, `PunchAuditEntry`, and both assignment
  entities; re-index with `client_id` leading — `punches(client_id, employee_id, punch_time)`.
  Cheapest to do now, while `punches` is small.
- **Data-protection groundwork** (§5): storage encryption + `SSL Mode=Require`; separately-keyed
  backups; log redaction on employee/pay endpoints; a doc comment on `PayCalculationSnapshot` and
  `PayLineItem` recording that PII must never be denormalised into them. Reserve `employee_sensitive`
  as the landing place for Tier A fields — no table needed yet, just the decision written down.
- **Seed/demo data.** You cannot build or demo a configuration UI against an empty database. A
  seeder producing one client, ~20 employees, several positions, two or three pay rules on different
  templates, and a few weeks of punches is a Phase 0 deliverable, not an afterthought — Phases 2–4
  are unworkable without it, and it doubles as integration-test fixture data.
- **Decide the API→engine project reference.** `GET /metadata/premium-rules` (§3) and the Phase 4
  what-if (§7) both need `TimeCalculation` referenced from the API. The `.csproj` comment guards this
  boundary deliberately, so cross it on purpose and update the comment — don't let it happen by
  accident in a later PR.
- Persist `DifferentialRule` (+ `ClientId`, + `PayRule` association) and `HolidayCalendar` (D, G).
- `GET /metadata/premium-rules` from `PremiumRegistry`; API references the engine project.
- `ProblemDetails` everywhere; one validation-error shape.
- OpenAPI build-time document generation; CORS for the Vite dev origin.
- Integration tests — `Program` is already `public partial`, so `WebApplicationFactory` is ready to go.

### Phase 1 — Users, auth, tenancy *(backend only)*
- `TimeCalculation.Identity` project, `AppUser`, four roles, seed a `SystemAdmin`.
- Email + password login via `MapIdentityApi`; cookie scheme; authorization written against
  **policies and claims** so a bearer scheme can be added later without rework (§5).
- Resolve `_tenantClientId` from the principal — **activates the dormant filters (J)**.
- **Rework the tenant filters**: drop the `_tenantClientId == null ||` escape hatch, make the tenant
  id required, add filters to `punches` / `punch_audits` / assignment tables (§5).
- Drop `CreatedBy` from request contracts; source it from the authenticated user, and populate
  `PunchAuditEntry.ActorUserId`.
- **Isolation test suite** — for every tenant-scoped entity, prove a `ClientAdmin` for client A
  cannot read, update, or delete client B's row. Table-driven over the entity list, so a new entity
  without a filter fails the build rather than shipping a leak.
- Capture the generated SQL for the top read paths in a test and assert `client_id` appears as a
  plain equality predicate — this is what stops the sargability regression from creeping back.

### Phase 2 — Frontend foundation
Scaffold, codegen pipeline, app shell + nav, login/logout, route guards, and **Clients CRUD
end-to-end as the reference pattern** — list, detail, form, validation, optimistic update, error
handling. Every later feature copies this. Playwright smoke: log in, create a client, edit it, log out.

### Phase 3 — People
Employee list (server-side paged/filtered) · employee detail tabs · position CRUD ·
`<EffectiveDatedTimeline>` built here and used for position assignments · self-service profile edit
scoped by role.

### Phase 4 — Pay rules
Pay rule list + editor with the three-tier taxonomy · template picker + "modified" indicators ·
version history view (reading what Phase 0 made possible) · pay rule assignment via the timeline
widget · **single-employee what-if diff** (§7).

> **PR/OR/WA templates are out of scope until a client needs them — deliberately, not blocked.**
> `PLAN.md` §6's override matrix still has `?` rows for **PR, OR, and WA** waiver policies
> (`PERF_FIXES_PLAN.md` Part 3 flagged them pending legal review). Rather than gate Phase 4 on
> review that has no scheduled trigger, ship the template picker with only *Federal*, *California*,
> and *Colorado* — the three whose override matrices are actually filled in. Build the template
> picker so adding a fourth is a data change, not a code change (§6 Rule 3 already models this as
> template + version, not a hardcoded switch), and treat "sales is about to close a PR/OR/WA
> client" as the trigger to open the legal review and add that template. Until then, those states
> are simply absent from the picker — no half-built, no-waiver-support state to accidentally ship.

### Phase 5 — Advanced configuration
Differential rule editor (the `DayScheduleMode` modes are mutually exclusive — the form must be a
mode selector that swaps its body, not five ANDed filter sections; `PipelineContext` actively
rejects a single-day `ConsecutiveDayRange`, so the UI should too) · premium selection UI backed by
the metadata endpoint · holiday calendar management · state minimum wage table.

### Phase 6 — Employee self-service
Punch entry · own timecard view · punch edit with audit trail surfaced · supervisor punch approval
and premium override screens (needs Gap I closed).

### Phase 7 — Full impact preview
Worker queue (open decision #6) · client-wide impact jobs · per-employee/per-shift diff drill-down ·
"what changed and why" explanation trail.

---

## 9. Decisions

Settled — say the word and I'll rework any of them.

**Mine (tell me if you disagree):**

1. **Monorepo** — `RobTimeUI/` inside the RobTime repo, not a separate repo.
2. **`openapi-typescript` + `openapi-fetch`** over NSwag/Kiota/hey-api. Types generated, client hand-written and thin.
3. **Separate `TimeCalculation.Identity` project** rather than putting Identity in `TimeCalculation.Persistence`.
4. **`@js-joda/core`** for dates rather than `date-fns`/`dayjs`/`Temporal`.
5. **Four roles** — SystemAdmin / ClientAdmin / Supervisor / Employee.
6. **Pay rule versioning is Phase 0, not Phase 7**, even though the feature that needs it ships last.
7. **Global query filters over per-query filtering** for tenancy — with the `== null ||` shape removed (§5).
8. **No column-level encryption now** — storage encryption + access control + audit instead, with a
   named tripwire (SSN/bank/DOB) that flips it, and a reserved table so flipping it stays cheap (§5).

**Yours (answered):**

8. **Cookie auth**, with a documented exit path to bearer tokens (§5). Implies same-origin deployment for now.
9. **Email + password for everyone**, including employees. Timeclock + badge number is a later
   addition, tracked in §5 and §11.
10. **Template is a mandatory starting point; every field editable afterwards.** Requires template
    lineage tracking so "customised" stays visible (§6, Rule 3).
11. **True SaaS multi-tenant** — isolation is a correctness requirement, and Phase 1 ships a test
    suite that proves it (§5).
12. **No approval step** on pay rule changes; save-is-live. The `Draft` status still lands in Phase 0
    because impact preview needs it — which means approval stays cheap to add later (§11).

## 10. Follow-on questions

None blocking. Three that will want answers before their phase:

1. **Client switching for `SystemAdmin`** — in true SaaS, does a SystemAdmin impersonate/scope into
   one client at a time (simple, safe, and the tenant filter keeps working), or see cross-client
   aggregate views (needs `IgnoreQueryFilters` paths and a separate read model)? Phase 2 nav design
   depends on it. I'd start with scope-into-one.
2. **Client self-signup, or do you onboard them?** Determines whether Phase 1 needs a registration
   flow and email verification at all, or just SystemAdmin-creates-ClientAdmin.
3. **Where do employees get their initial password?** Admin-set temporary password vs. emailed invite
   link. The invite flow is more work but is the only sane answer at any real headcount — and it
   becomes moot for shop-floor staff once badge auth lands.
4. **Should a `Supervisor` see wage rates and pay amounts?** They approve punches and premium
   overrides; neither requires knowing what anyone earns. I'd say no — but it's a policy call, and
   it's much easier to decide now than to claw back a permission later. Blocks the Phase 3 employee
   detail screen (§5, data protection).

## 11. Future improvements

Deliberately deferred. Recorded here so the design doesn't accidentally preclude them.

| Item | Notes | Design constraint it implies today |
|---|---|---|
| **Timeclock devices + badge numbers** | Registered device + `Employee.BadgeNumber`, clock-only credential. `Punch.DeviceId`/`DevicePunchId` and the unique idempotency index already exist. | Auth must be multi-scheme-ready: authorize on policies/claims, never on cookie presence (§5). |
| **Pay rule change approval workflow** | Submit → review → activate, with the impact preview attached to the review. `PLAN.md` §9 item 14 flags the same for timecard approval. | `PayRule.Status` already has `Draft`; leave room for `PendingApproval` between `Draft` and `Active`. |
| **Enterprise SSO (SAML/OIDC)** | Table stakes for larger SaaS customers. | Per-client auth configuration; `AppUser` must tolerate having no local password. |
| **Public / partner API** | Payroll exports, HRIS sync. | Bearer tokens + API keys as an additional scheme. |
| **Postgres RLS** | Defense-in-depth under the EF filters, not instead of them (`PLAN.md` open decision #5). | Denormalized `ClientId` on every tenant-scoped table (Phase 0) is the prerequisite either way. |
| **Column-level encryption for Tier A PII** | Triggered by SSN / bank details / DOB arriving — i.e. by W-2 or direct deposit (§5). | Tier A data lands in `employee_sensitive`, never as columns on `employees`. Per-tenant keys from day one. |
| **SOC 2 Type II** | Expect an enterprise customer to demand it before any regulator does. | Read auditing on pay data, log redaction, and key management are the controls that take longest to retrofit. |
| **Timecard approval** | Manager signs off before payroll runs. | `PLAN.md` §9 item 14 — model shouldn't preclude it. |
| **Effective-dated `Employee.State`** | `PLAN.md` §9 item 12 — employee moves CA→NV mid-period. | Reuse `<EffectiveDatedTimeline>`; no new UI concept. |
| **Bulk employee import** | CSV onboarding for a new client. | Response DTOs and validation shapes should be reusable per-row, not just per-request. |
| **Punch geofencing / IP restriction** | Explicitly out of scope in `PLAN.md`. | None — device registration is the natural hook when it arrives. |
