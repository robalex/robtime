# RobTimeUI — Frontend & API Plan

Companion to `PLAN.md` (the engine plan) and `DEPLOY_PLAN.md` (AWS deployment, added 2026-07-22).
`PLAN.md` declared "HTTP API, UI" out of scope; this one brings them into scope. Read `CLAUDE.md`
first for the engine architecture.

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
| **I** | **`WaiverPolicy` is hardcoded per premium-rule class, identical for every client.** `CaMealPremiumRule.WaiverPolicy`, `PrMealPremiumRule.WaiverPolicy`, etc. return a fixed enum value; there is no table, so no client can hold a different policy than RobTime's default — and for `PR_MEAL`/`OR_MEAL`/`WA_MEAL` that default is an explicit unverified guess (`// TODO: verify {state} waiver rules`). | **Decided 2026-07-22: make waiver policy client-configurable instead of RobTime asserting it.** Safe default (`NotWaivable`) stays RobTime's suggestion; a client can loosen it only through an explicit attestation step, effective-dated and audited — so the legal determination is the client's, documented, not RobTime's. This supersedes the "defer legal review" call from earlier the same day: RobTime no longer needs its own answer to ship PR/OR/WA templates, because it isn't asserting one. Applies to **all six** premium rules, not just the three unconfirmed ones — a CBA can legitimately override even a "confirmed" state default. See the Phase 4/5 notes below. **Separately**, per-*occurrence* overrides (`OverrideKind` — a supervisor/employee waiving one specific shift's premium, distinct from the client-wide policy) still have no table and are still Phase 6 work. |
| **J** | **Multi-tenancy is wired but inert.** `PayrollDbContext` accepts `tenantClientId`, but `AddDbContext` never supplies one, so every filter short-circuits to "no filter." | Auth is what turns it on. Until then, any caller sees every client's data. |

Gaps **E**, **F**, and **I**'s policy-persistence half are model changes. They are cheap now and
expensive after there's a UI and production data shaped around the current behaviour. Do them in
Phase 0.

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
  TimeCalculation.Api/          # emits openapi/TimeCalculation.Api.json on build
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
dotnet build                      →  TimeCalculation.Api/openapi/TimeCalculation.Api.json
npm run gen:api                   →  src/api/schema.d.ts        (openapi-typescript)
openapi-fetch createClient<paths> →  fully typed paths/params/bodies/responses
```

**Setup — done 2026-07-23, with two wrinkles worth flagging (verified empirically, not assumed;
the property names really had shifted since .NET 8/9 the way this section originally warned):**

1. `Microsoft.Extensions.ApiDescription.Server` added to `TimeCalculation.Api.csproj`, with
   `<OpenApiDocumentsDirectory>openapi</OpenApiDocumentsDirectory>`. `AddOpenApi()` was already
   registered in `Program.cs`.
   - **Output filename is `TimeCalculation.Api.json`, not `v1.json`.** The generator names the file
     after the project, not the document (the document's own internal name is still "v1" — that's
     what the *runtime* `/openapi/v1.json` endpoint is named after, a separate, already-working
     thing served by `MapOpenApi()`). No supported flag forces a different output filename without
     fighting the vendored `.targets` file, so the plan adjusted to the real name instead — point
     `gen:api` at what's actually on disk.
   - **Generation is explicit, not build-time — flipped 2026-07-23.** Originally wired up with
     `<OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>`, but that boots
     `Program.cs`'s full composition root via `HostFactoryResolver` (the same mechanism `dotnet ef`
     uses for migrations) on *every* build to introspect routes, which hits the eager `PayrollDb`
     connection-string check — a bare `dotnet build`/`dotnet test`/IDE build throws unless
     `ASPNETCORE_ENVIRONMENT=Development` is set first. That turned out to be real friction (a
     developer hit it blind, with no error text to go on, before realizing it was this). Since
     nothing consumes the generated doc yet (`RobTimeUI` doesn't exist), the property is now
     `false` and generation is a deliberate step instead:
     `ASPNETCORE_ENVIRONMENT=Development dotnet build TimeCalculation.Api -t:GenerateOpenApiDocuments`.
     `.github/workflows/ci.yml` no longer needs the `ASPNETCORE_ENVIRONMENT` env var at all — plain
     `dotnet build`/`dotnet test` just work now, with or without it.
2. UI `package.json`: `"gen:api": "openapi-typescript ../TimeCalculation.Api/openapi/TimeCalculation.Api.json -o src/api/schema.d.ts"`.
3. **`openapi/` is gitignored — it's a build artifact, only produced when the `GenerateOpenApiDocuments`
   target is explicitly invoked.** **Commit `schema.d.ts` instead** (once `RobTimeUI` exists) — a CI
   step regenerates it and fails on diff, which turns "someone changed the API and broke the UI" into
   a red build on the API PR, not a runtime 400 next week. The intermediate JSON has no reason to
   live in source control.

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

**Superseded 2026-07-23 — see "Auth mechanism" below.** Credentials now live in Amazon Cognito, not
a local `TimeCalculation.Identity` project. `AppUser` becomes a thin profile/authorization row —
no `IdentityUser<int>` base, no password hash, no separate DbContext — keyed by the Cognito `sub`
(stable across the user's lifetime, including if SSO federation is added later) instead of an
identity-owned int:

```csharp
class AppUser
{
    public required string CognitoSub { get; init; }  // Cognito's `sub` claim — the PK
    public int? ClientId { get; set; }                // null only for SystemAdmin
    public int? EmployeeId { get; set; }               // set when this user IS an employee
    public required string DisplayName { get; set; }
    public required AppRole Role { get; set; }
}
```

Lives directly in `TimeCalculation.Persistence` (a `DbSet` on `PayrollDbContext`) — the reason for a
separate project was isolating `Microsoft.AspNetCore.Identity.EntityFrameworkCore` from
`PayrollDbContext`; with no local Identity package, that reason is gone. One fewer project, one
fewer `DbContext`, one fewer migrations history table.

### Roles

| Role | Can |
|---|---|
| `SystemAdmin` | Everything, but **scoped into one client at a time** — creates clients and client admins, then works within a selected client like a `ClientAdmin` would. Rare. |
| `ClientAdmin` | Everything within one client: employees, positions, pay rules, differentials, users. |
| `Supervisor` | View/edit punches for their client, approve premium overrides, **and sees wage rates and pay amounts**. Read-only on config. |
| `Employee` | Own punches, own profile. Read-only on everything else. |

This is the smallest set that covers what you described (employee self-service + at least one
admin) while leaving room for the supervisor override workflow `PLAN.md` §6 already models. Start
with role-based checks behind a `lib/permissions.ts` shim so moving to claims/policies later is a
one-file change.

**`SystemAdmin` scoping (decided 2026-07-22):** one client at a time, never a cross-client view by
default — "creates clients" is the only action that's inherently cross-tenant; everything else a
`SystemAdmin` does happens inside a selected client, same permission surface as `ClientAdmin`. This
keeps every tenant filter from §5 correct with zero exceptions: a `SystemAdmin` session just carries
whatever `ClientId` they've currently selected, the same `_tenantClientId` every other role uses.
Cross-client aggregate dashboards/reports are wanted eventually but are a distinct, later capability
— see §11 — not a `SystemAdmin` permission. When they land, build them as an explicit reporting path
(`IgnoreQueryFilters` behind its own audited endpoint), not as a loosening of the per-request filter.

#### How the selection actually travels (planned 2026-07-25, not yet built)

The decision above says a `SystemAdmin` session "carries whatever `ClientId` they've currently
selected" — this is the missing mechanism. Surfaced concretely when the Clients UI landed: because
the `Client` tenant filter is `c.Id == _tenantClientId` and a `SystemAdmin` carries no
`custom:client_id` claim, every by-id read 404'd for them (fixed by an explicit `VisibleTo` bypass in
`ClientService`, but that only papers over `Client` itself — the same hole reopens for *every* entity
the moment a `SystemAdmin` needs to manage one, which is Phase 3).

**The selection is a request header, not a claim.** `X-RobTime-Client-Id`, attached by the same
openapi-fetch middleware that attaches the bearer token. `HttpContextTenantContextAccessor` resolves
`_tenantClientId` as: *if the role claim is `SystemAdmin`, read the header; otherwise read
`custom:client_id`, always.*

**The security property, stated so it can be tested:** a non-`SystemAdmin`'s header is ignored
outright — never merged with the claim, never a fallback when the claim is absent. Without that rule
this is a one-header cross-tenant read for any authenticated user, so it needs a test that a
`ClientAdmin` sending another client's id still sees nothing. A `SystemAdmin` with no selection
resolves to null and sees nothing — fail closed, same as today.

Rejected alternatives, and why:
- **Update the `custom:client_id` claim on switch.** Needs an `AdminUpdateUserAttributes` call plus a
  token refresh per switch, and conflates identity (whose client am I?) with transient session state
  (which client am I looking at?). `AppUser.ClientId` stays null for a `SystemAdmin` for the same
  reason — selection is not identity.
- **Server-side "current selection" per user.** A write on every switch, a read on every request, and
  it makes the selection follow you across devices — the opposite of useful.
- **A `/c/:clientId/...` URL segment.** Genuinely the strongest option on the merits: explicit,
  linkable, back-button-correct, and the client id lands in the query key so cache separation is
  automatic. Rejected because it restructures the entire route tree and puts a redundant id in front
  of `ClientAdmin`s, who are the overwhelming majority of users. Worth revisiting if deep-linking
  across tenants ever becomes a real workflow.

**Consequences to handle when building it:**
- **Switching clients must clear the query cache** (`queryClient.clear()`). Every cached list is
  tenant-scoped; keeping the cache across a switch renders one client's data while scoped to another
  — a cross-tenant leak in the UI even though the API behaved correctly.
- **Selection lives in `sessionStorage`**, per tab, so two tabs can be scoped to different clients
  (genuinely useful for a `SystemAdmin` comparing configurations) — and so it can't outlive the tab.
- **`GET /me` should return the effective client** (id + name). The UI needs it to show current scope,
  and it's the only way to distinguish "nothing here yet" from "your selection points at a client
  that was deleted" — otherwise a stale selection silently renders empty screens everywhere.
- **`ClientService.VisibleTo`'s bypass narrows to List and Create.** Those are inherently
  pre-selection (listing clients is *how* you choose one). With a selection in hand,
  `GET /clients/{id}` works through the ordinary filter, so the special case shrinks rather than
  spreading.
- **Creating a client auto-selects it**, which is what the current create-then-navigate flow already
  assumes.
- **`TestAuthHandler` needs header support** so the isolation suite can cover both the permitted and
  the spoofed case.

**Sequencing: build this before Phase 3.** Phase 3 is where a `SystemAdmin` first needs to manage
employees inside a client, and every entity screen written before the selector exists would need
revisiting after it.

**Supervisor wage visibility (decided 2026-07-22):** `Supervisor` sees wage rates and pay amounts.
Anticipate a second, more restricted tier later (a `Supervisor` who approves punches without seeing
pay) — see §11. Don't build that tier speculatively now; when it's needed, it's a fifth role name
plus a `lib/permissions.ts` branch, not a redesign, because permissions are already centralized
there rather than scattered through components.

### Login: email + password — via Cognito (decided 2026-07-23)

Everyone who needs web access — employees included — signs in with email and password for now,
handled by an **Amazon Cognito User Pool** rather than `MapIdentityApi<AppUser>()`/local ASP.NET
Core Identity. See "Auth mechanism" below for the full rationale and what this changes in Phase 1.

**Not every `Employee` needs an `AppUser`/Cognito account.** `AppUser.EmployeeId` is nullable
specifically because badge-only shop-floor workers (below) never log into the web app at all — an
`AppUser` only gets provisioned for someone who needs self-service access.

> **TODO — timeclock authentication.** Employees on a shop floor will not log in with email and
> password on a shared wall-mounted clock. Later we want a **timeclock concept**: a registered
> device, plus an **employee badge number** the employee enters (or swipes) *only at a clock*. The
> badge number is a clock-only credential — it must never be accepted by the web app, and it does
> not replace the employee's own login. The data model already anticipates the device half of this:
> `Punch.DeviceId` / `Punch.DevicePunchId` exist with a unique index for idempotent ingest
> (`PLAN.md` §9 item 9). What's missing is a `Device` registration table, `Employee.BadgeNumber`
> (unique per client), and a separate auth scheme for device endpoints. See §11.
>
> **Clarified 2026-07-23 — this never touches Cognito.** Badge punching and Cognito login are two
> unrelated identities, and the design must not conflate them:
> - **The device authenticates, not the employee.** A registered `Device` holds its own credential
>   (an API key or client-credentials grant, issued at registration, scoped to one client tenant).
>   That's the actual security boundary — only a trusted, registered device can submit punches.
> - **The badge number is a lookup key, not an identity assertion.** Once the device is
>   authenticated, it sends `BadgeNumber` + punch data; the API resolves `EmployeeId` via
>   `(ClientId, BadgeNumber)` — a plain query against `Employee`, never a JWT claim or a Cognito
>   user lookup. The badge-punching employee may have no `AppUser`/Cognito account at all.
> - **Audit trail implication:** `PunchAuditEntry.ActorUserId` assumes an authenticated principal;
>   a badge punch doesn't have one (it has a device + a badge-resolved employee instead). The audit
>   row needs to record *how* the punch was authenticated (device+badge vs. self-service login), not
>   just squeeze the badge-resolved employee into `ActorUserId` — a payroll auditor will care about
>   that distinction. Deferred to the Phase 6 device work; noted here so it isn't lost.

### Auth mechanism: Amazon Cognito + JWT bearer (decided 2026-07-23, supersedes cookie auth)

**Cognito User Pool replaces local ASP.NET Core Identity entirely.** No `TimeCalculation.Identity`
project, no `AppIdentityDbContext`, no local password storage — Cognito owns credentials, MFA,
password reset, and (later) SAML/OIDC federation for enterprise SSO. This was a direct pivot from
the original cookie-auth decision below, prompted by pricing Cognito for the SSO question this
project will face as real SaaS customers show up: Cognito's **Lite tier is free for the first
10,000 MAU/month** and supports SAML/OIDC federation at every tier, so there's no early-stage cost
argument for building local auth first and migrating later — migrating identity providers *after*
real customer passwords exist is the expensive path, not the cheap one.

**This means JWT bearer auth from Phase 1, not cookies deferred to later.** Cognito issues JWTs, not
cookies — the SPA holds the access token in memory (never `localStorage`, which is readable by any
XSS) and refreshes via Cognito's refresh-token flow. The API validates tokens via
`AddJwtBearer`/`AddAuthentication` against Cognito's JWKS endpoint
(`https://cognito-idp.{region}.amazonaws.com/{userPoolId}/.well-known/jwks.json`). The three reasons
cookies were originally expected to be outgrown — timeclock devices, enterprise SSO, a future
public/partner API — are exactly why adopting Cognito now means building the bearer-token path once,
rather than building cookie auth first and doing this migration a second time.

**Claims carry `client_id`/`role`, not a DB round trip per request.** `AppUser`'s `ClientId`/`Role`
are set as Cognito custom attributes (`custom:client_id`, `custom:role`) at provisioning time and
mapped into ID/access token claims — `_tenantClientId` is resolved straight from the validated JWT
when constructing `PayrollDbContext`, same as the cookie design intended, just sourced from a claim
instead of a cookie-backed identity ticket. A change to a user's role takes effect on their next
token refresh, not instantly — acceptable for now; revisit with a Pre Token Generation Lambda
(computing claims live from the `AppUser` row at issuance) only if that staleness actually bites.

**`ASP.NET Core supports multiple authentication schemes side by side` still holds and still
matters** — the device/badge scheme from the TODO above is a second, independent scheme alongside
Cognito JWT bearer, not a variant of it. Authorization stays written against policies and claims,
never against "which scheme authenticated this request," so a `/api/device/*` scheme is additive
whenever Phase 6 needs it.

**Two new mechanics this introduces, without a Testcontainers-style equivalent for Cognito:**
1. **User provisioning is a two-system write.** `POST /users` (ClientAdmin-only) must call Cognito's
   `AdminCreateUser` *and* insert the local `AppUser` row, in that order, with a defined
   compensating action if the second write fails after the first succeeds.
2. **Integration tests can't spin up a real Cognito pool per run.** `TimeCalculation.Api.Tests`
   needs a fake JWT-bearer test-authentication handler that mints trusted test tokens/claims,
   bypassing real Cognito token validation in the test environment — the same role `ApiFixture`
   plays for Postgres via Testcontainers, just without a real backing service to spin up.

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
   in payroll. **Decided 2026-07-22: `Supervisor` sees wage rates and pay amounts** — not the
   restrictive default I'd suggested, so item 6 below (read auditing) matters more here than it
   would have otherwise. A second, more restricted supervisor tier is anticipated later (§11); the
   role matrix (above) already notes this is a `lib/permissions.ts` branch when it's needed, not a
   redesign.
6. **Read auditing on sensitive data.** Who looked at whose pay, and when. For insider misuse this is
   worth more than any encryption scheme, because encryption doesn't protect against a user with
   legitimate credentials — and it's the control that actually matters now that `Supervisor` has
   standing wage visibility rather than none.
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
| Time | Self-service clock (responsive, logged-in) · Timecard view (role-gated `PayResult` render) · Supervisor pending-request queue — all *(Phase 6)* · Shared kiosk clock (tablet, ship-gated on badge auth §11) |

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
- [x] **Answer §10 Q1 and Q4 — done 2026-07-22.** `SystemAdmin` scopes into one client at a time,
      never cross-client (cross-client dashboards are a distinct future capability, §11).
      `Supervisor` sees wage rates and pay amounts, with a restricted tier anticipated but not built
      (§11). Both written into §5's role matrix and §9 as decisions 15–16.
- [x] **Legal review of premium waiver policies — superseded, not just deferred.** First decided
      2026-07-22 to defer until a PR/OR/WA client showed up; revised the same day to a better fix:
      **waiver policy becomes client-configurable** (Gap I) instead of something RobTime asserts.
      RobTime never needs to answer "is this waivable in Oregon" — the client does, explicitly,
      with an audited attestation, defaulting to the conservative `NotWaivable`. `PLAN.md` open
      decision #1 stays open, but it's no longer a blocker for anything in this plan.
- [x] **`.gitignore` — done 2026-07-22.** Added `RobTimeUI/{node_modules,dist,.vite,*.local}`
      ahead of the folder existing.

### Phase 0b — AWS deployment foundation *(runs parallel to Phase 0; see `DEPLOY_PLAN.md`)*
Terraform bootstrap (remote state + GitHub OIDC role) · network/database/frontend modules ·
`staging` environment, no custom domain yet. No code dependency on Phase 0 except the eventual
Dockerfile — can start immediately. First real deploy happens once Phase 0 has a working API to
containerize.

### Phase 0 — API foundation *(backend only)*

**Model/schema sub-phase done 2026-07-22** (all verified: 296/296 tests, clean `--no-incremental`
build, API smoke-tested against the fresh schema — a real `POST /clients` round-tripped through the
new migration end-to-end):

- [x] Model changes: `PayRule.Name`/`Description` (H), `EmployeePositionAssignment.Rate` (E) —
      threaded through `PipelineContext.GetRateAt` (new) and `PairPositionAndRateAttacher`, which
      now prefers the assignment's own rate over `Position.BaseRate`, **`PayRule` versioning +
      effective dating + draft status (F)**, `PayRule.TemplateCode`/`TemplateVersion`.
      Versioning design: `PayRule` gained `RuleFamilyId` (stable across a rule's edit history — by
      convention equals the first version's own `Id`), `Version` (now starts at 1), `Status`
      (`Draft`/`Active`/`Superseded`), and its own `EffectiveFrom`/`EffectiveTo` — bookkeeping for
      the version-history UI only, **not** consulted by the calculation pipeline, which still
      resolves the applicable rule purely through `PayRuleAssignment`'s dates. This was a
      deliberate choice to land the versioning *fields* without touching `PipelineContext.GetRuleAt`
      at all — the actual "create a new version, don't mutate" *workflow* is CRUD-endpoint work,
      not schema work, and is still ahead of us.
- [x] **Tenancy schema prep** (§5): `ClientId` added to `Punch`, `PunchAuditEntry`, both assignment
      entities, plus FK constraints (`Restrict`) on all of them. Every hot index re-indexed with
      `client_id` leading (verified by inspecting the generated migration directly, not just
      trusting the C# config). **Deliberately stopped at schema** — no `HasQueryFilter` predicates
      were added or changed on these four; that's explicitly Phase 1 ("rework the tenant filters"),
      once there's a real `_tenantClientId` to filter on. `EmployeePositionAssignmentEntity`'s
      existing filter (via the `Position` navigation) was left untouched for the same reason, even
      though the new direct `ClientId` column could simplify it — that's a filter-predicate change,
      bundled into Phase 1's uniform pass instead of touched twice.
- [x] Persist `DifferentialRule` (+ `ClientId`) and `HolidayCalendar` (D, G). `HolidayCalendar`
      gained `Id`/`ClientId`/`Name` and a settable `Dates` while keeping its existing constructor
      (so `HolidayCalendar.UsFederal(year)` and every existing call site still work unchanged).
      `PayRule.ActiveDifferentialCodes` added, mirroring `ActivePremiumCodes` — a client's pay rule
      opts into a subset of that *client's own* differentials, not a fixed registry.
- [x] **Persist client-configurable waiver policy (Gap I) — schema only, as scoped.** New
      `ClientPremiumPolicy(Id, ClientId, PremiumCode, WaiverPolicy, SetBy, SetAt, EffectiveFrom,
      EffectiveTo, Justification?)`, EF-mapped with a resolution index on
      `(ClientId, PremiumCode, EffectiveFrom)`. **Explicitly not wired into `WaiverEvaluator` yet** —
      resolving "client override as of the calculation date, else the rule's built-in default" is
      real pipeline behavior change that deserves its own dedicated, tested pass, not a rider on a
      schema change. Tracked as a clear follow-up, not silently dropped.
- [x] **Clean migration regen, decided together 2026-07-22** (no production data exists anywhere).
      Dropped the local dev database, deleted the old `Initial` migration + snapshot, regenerated a
      fresh `Initial` against the full target schema (12 tables), applied it, and confirmed via
      `dotnet ef migrations list`/`dbcontext info` — no pending migrations. Also updated the
      `PersistenceModelTests` suite: fixed the one test pinning the old `(employee_id, punch_time)`
      index shape, and added coverage for every new FK, index, and query filter this pass touched
      (11 new tests) so none of this schema is unpinned going forward.

**API-surface sub-phase done 2026-07-23** (296/296 tests throughout; CORS and ProblemDetails
smoke-tested live against a running instance, not just compiled):

- [x] **Decided the API→engine project reference.** Crossed deliberately — `TimeCalculation.Api.csproj`
      now references `TimeCalculation`, with the `.csproj` comment updated to say why (the metadata
      endpoint below, and the Phase 4 what-if later) rather than silently dropping the old guard comment.
- [x] **`ProblemDetails` everywhere; one validation-error shape.** `builder.Services.AddProblemDetails()`
      plus `app.UseExceptionHandler()` outside Development, so even an unhandled exception comes back
      `application/problem+json` instead of a bare 500. The two endpoints that returned bare
      `NotFound<string>`/`Conflict<string>` (Employee, PayRule, Punch) now return `TypedResults.Problem(...)`
      instead — verified live: both validation and not-found responses now carry the same
      `type`/`title`/`status`/`detail` shape.
- [x] **CORS for the Vite dev origin.** A named `ViteDev` policy (`http://localhost:5173`,
      credentialed — cookie auth needs that), applied only in Development. Deliberately not a
      general-purpose policy to widen later: production serves the SPA same-origin behind
      CloudFront (§5's cookie-auth design), which needs no CORS policy at all — delete this once
      same-origin proxying exists in dev too, don't grow it. Verified live: allowed origin gets
      `Access-Control-Allow-Origin` echoed back with credentials; a disallowed origin gets nothing.
- [x] **OpenAPI build-time document generation — with two corrections to what this section
      originally assumed, both found by actually running it, not by reading docs:**
      1. The output file is `openapi/TimeCalculation.Api.json`, not `v1.json` — the generator names
         the file after the project, not the document. `gen:api` and the file tree above were wrong
         until this pass; fixed.
      2. **The doc generator boots the full `Program.cs` composition root** (via `HostFactoryResolver`,
         same mechanism `dotnet ef` uses) to introspect routes — which means, with
         `OpenApiGenerateDocumentsOnBuild=true`, a bare `dotnet build` threw on the eager `PayrollDb`
         connection-string check, because the build-time environment defaults to `Production`, which
         has no committed connection string by design. Documented at the time as "set
         `ASPNETCORE_ENVIRONMENT=Development` first" — but a developer hit this blind later the same
         day with no error text to go on, which showed the workaround-and-document approach wasn't
         good enough: it made the *default* inner-loop build fragile for a feature (`RobTimeUI`
         codegen) that doesn't exist yet and consumes nothing. Reversed: `OpenApiGenerateDocumentsOnBuild`
         is now `false`, generation is explicit
         (`ASPNETCORE_ENVIRONMENT=Development dotnet build TimeCalculation.Api -t:GenerateOpenApiDocuments`),
         and CI no longer sets `ASPNETCORE_ENVIRONMENT` at all — a plain `dotnet build`/`dotnet test`
         just works now, everywhere, with no special incantation.
      `openapi/` itself is gitignored — a build artifact, only produced when that target is explicitly
      invoked, not something to commit (`schema.d.ts` is, once `RobTimeUI` exists).

**Phase 0 fully closed 2026-07-23.** Everything below landed, in order, each verified live (not
just compiled) before moving to the next: 321/321 tests passing (300 engine/persistence + 21
integration), 0 warnings under the same `TreatWarningsAsErrors` CI uses.

- [x] **Response DTOs for every entity** (Gap C) — endpoints no longer return EF entities.
- [x] **Full CRUD** — `GET` list (paged, `search`/`clientId`/`status` filters depending on entity),
      `GET` by id, `PUT`, soft-delete (`DELETE`) — Client, Employee, Position (built from scratch —
      it had zero endpoints before this), PayRule (Gap B). `PayRule`'s Update/Delete additionally
      enforce the Draft-only mutation rule from Gap F (§7) — Active/Superseded rules 409 rather than
      silently accepting a retroactive edit; the `RuleFamilyId == Id` convention from that same gap
      is now actually implemented via a two-phase save, not just documented.
- [x] **Soft delete** — `IsDeleted` on all four entities, each backed by two independent EF Core 10
      named query filters (`Tenant`, `SoftDelete`) rather than one combined lambda; verified live
      that named filters genuinely AND together instead of the second call silently overwriting the
      first.
- [x] **`GET /metadata/premium-rules`** — `IPremiumRule` gained `Name`/`Description` (all six state
      rules), read from `PremiumRegistry` with no DB dependency.
- [x] **Data-protection groundwork** (§5) — `SSL Mode=Require` documented for production's
      connection string (not committed here, so nothing to mechanically enforce it *on* yet); doc
      comments on `PayCalculationSnapshot`/`PayLineItem` guarding against a future "just add
      EmployeeName for convenience" regression; a note against ever calling
      `EnableSensitiveDataLogging()`.
- [x] **Seed data** — `dotnet run -- --seed` populates 1 client, 4 positions, 2 pay rules
      (Federal + California, both Active), 12 employees, 100 punches; verified by querying the
      result back through the live API, not just checking it ran.
- [x] **Integration tests** — new `TimeCalculation.Api.Tests` project,
      `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql` (a real, ephemeral Postgres per
      run, not an in-memory provider), 21 tests. Caught a real bug in the process:
      `ActivePremiumCodes`/`ActiveDifferentialCodes` were typed `IReadOnlySet<string>` on the wire
      DTOs, which `System.Text.Json` can serialize but not deserialize without a custom converter —
      invisible to every curl-based smoke test in this doc (none of them round-tripped a response
      back into a typed object), guaranteed to break any real .NET client. Fixed to `HashSet<string>`.

Two things surfaced along the way and fixed on the spot, not deferred: `[AsParameters] PagingQuery`
treated `Page`/`PageSize` as *required* query parameters despite their C# property-initializer
defaults (`[AsParameters]` binds by each property's own nullability, not the record's defaults) —
a bare `GET /clients` 400'd until this was caught live. And `Punch.ClientId` had carried an FK
constraint since the tenancy-schema-prep work several commits earlier, but nothing was ever setting
it — every `POST /punches` had been failing 100% of the time since that migration landed, because
that turn's smoke test only exercised `/clients`. Both are exactly the class of bug a live
verification habit exists to catch before it reaches a commit, and both were caught by one.

### Phase 1 — Users, auth, tenancy *(backend only)*

**Revised 2026-07-23 for Cognito** (see §5 "Auth mechanism") — no local `TimeCalculation.Identity`
project; `AppUser` is a thin profile/authorization row, not a credential store.

**Application-code portion closed 2026-07-24** — everything that doesn't require a live Cognito
pool, built and verified against the fake JWT-bearer test handler exactly as the sequencing note
below anticipated:

- [x] `AppUser` entity (`CognitoSub` PK, `ClientId`, `EmployeeId`, `Role`) as a `DbSet` on
      `PayrollDbContext`, seeded a placeholder `SystemAdmin`. No EF Identity package, no separate
      `DbContext`.
- [x] `Microsoft.AspNetCore.Authentication.JwtBearer` wired against Cognito's JWKS endpoint (config
      is a placeholder — see below); authorization written against **policies and claims**
      (`client_id`, `role`) via `AuthorizationPolicies`, never against "is there a cookie."
      `RequireAuthorization()` applied to every endpoint; Client list/create is SystemAdmin-only
      through a dedicated policy + `IgnoreQueryFilters()` at that one legitimately cross-tenant call
      site (§5's SystemAdmin-scoping decision).
- [x] `_tenantClientId` resolved from the validated JWT's `client_id` claim via
      `ITenantContextAccessor`/`HttpContextTenantContextAccessor` — **activates the dormant filters (J)**.
- [x] **Tenant filters reworked**: the `_tenantClientId == null ||` escape hatch is gone everywhere
      (Client/Employee/Position/PayRule/DifferentialRule/HolidayCalendar/ClientPremiumPolicy/AppUser);
      filters added to `punches`/`punch_audits`/`pay_rule_assignments` (previously missing
      entirely); `employee_position_assignments` switched from filtering via the `Position`
      navigation to its own `ClientId` column. An unfiltered context now sees nothing, not
      everything — fail closed.
- [x] `CreatedBy` dropped from `CreateClientRequest`/`CreatePunchRequest` — sourced from the
      authenticated principal's `sub` claim instead.
- [x] **`POST /users` provisioning endpoint** — `UserProvisioningService` calls
      `ICognitoUserProvisioner.CreateUserAsync` (real implementation: `AdminCreateUser` via
      `AWSSDK.CognitoIdentityProvider`) then inserts the local `AppUser` row; on a `DbUpdateException`
      from the local write, best-effort compensates with `AdminDeleteUser` before rethrowing — no
      saga/outbox, so a compensation failure leaves an orphaned Cognito user needing manual cleanup,
      flagged in the code rather than silently assumed away. `ClientAdmin` can only target their own
      `ClientId`; `SystemAdmin` can target any (bootstrapping a new client's first `ClientAdmin` is
      the same cross-tenant exception Client creation gets).
- [x] **Fake JWT-bearer test-authentication handler** (`TestAuthHandler`) — mints trusted test claims
      from request headers, no real Cognito pool needed. `FakeCognitoUserProvisioner` does the same
      for the user-provisioning endpoint. All 22 pre-existing integration tests retrofitted to
      authenticate via `ApiFixture.CreateAuthenticatedClient`/`CreateClientAndScopedClientAsync`.
- [x] **Isolation test suite** (`TenantIsolationTests.cs`) — table-driven proof that all 12
      tenant-scoped entities are invisible cross-tenant, run directly against `PayrollDbContext`
      (not just through HTTP) so it also covers entities with no CRUD endpoint yet.
- [x] Generated-SQL sargability check on `punches`/`employees` — asserts `client_id` appears as a
      plain equality predicate, no `OR`.

**Found and fixed a real bug along the way, not just a test-infra wrinkle:** `Program.cs` was
capturing the `PayrollDb` connection string into a local variable *before* calling `AddDbContext`,
which silently defeated `WebApplicationFactory`'s Testcontainers override in
`TimeCalculation.Api.Tests` — every integration test in the project had been running against local
dev Postgres, not the isolated ephemeral instance `ApiFixture`'s own doc comment promised. The new,
more rigorous isolation tests were the first thing sensitive enough to expose it (they build a
`DbContext` directly from the Testcontainers connection string, bypassing DI, and saw an empty
database while the DI-resolved context serving every other test was quietly talking to local dev).
Fixed by reading the connection string lazily inside the `AddDbContext` callback — the standard fix
for this well-known `WebApplicationFactory` gotcha.

**Still open, blocked on the user's AWS credentials (same as `DEPLOY_PLAN.md` §4):**
- Terraform: Cognito User Pool + App Client (per environment), added to `infra/` alongside
  `DEPLOY_PLAN.md`'s other modules — Lite tier, custom attributes for `client_id`/`role`, no client
  secret (SPA app client). No `infra/` directory exists yet; nothing has been `terraform apply`'d.
- Once a real pool exists: replace the `appsettings.Development.json` placeholders
  (`Cognito:UserPoolClientId`, `Cognito:UserPoolId`, `Cognito:Region`) with real
  values, and verify `CognitoUserProvisioner`/the JwtBearer scheme actually work end-to-end — both
  are code-complete but unverified against a live pool.

### Phase 2 — Frontend foundation
Scaffold, codegen pipeline, app shell + nav, login/logout, route guards, and **Clients CRUD
end-to-end as the reference pattern** — list, detail, form, validation, optimistic update, error
handling. Every later feature copies this. Playwright smoke: log in, create a client, edit it, log out.

### Phase 2.5 — `SystemAdmin` client selector *(prerequisite for Phase 3)*
The mechanism §5 assumes but never specified: how a `SystemAdmin` session carries a selected
`ClientId`. Header-based (`X-RobTime-Client-Id`), resolved in `HttpContextTenantContextAccessor`,
with the header ignored outright for every non-`SystemAdmin` role. Client switcher in the app shell,
selection in `sessionStorage`, query cache cleared on switch, `GET /me` returning the effective
client. Full design, rejected alternatives, and the consequences to handle are in §5.

**Why it gates Phase 3:** a `SystemAdmin` can't manage a client's employees without first being
scoped into that client, and every entity screen built before the selector exists would need
revisiting afterwards. The Clients UI already hit the narrow version of this bug (see §5).

### Phase 3 — People
Employee list (server-side paged/filtered) · employee detail tabs · position CRUD ·
`<EffectiveDatedTimeline>` built here and used for position assignments · self-service profile edit
scoped by role.

### Phase 4 — Pay rules
Pay rule list + editor with the three-tier taxonomy · template picker + "modified" indicators ·
version history view (reading what Phase 0 made possible) · pay rule assignment via the timeline
widget · **single-employee what-if diff** (§7).

> **PR/OR/WA templates ship from day one — the client-configurable waiver policy (Gap I) is what
> unblocks this.** Previously excluded because offering the template meant RobTime implicitly
> asserting an unverified waiver rule. That's no longer true: the template presets
> `ActivePremiumCodes` including `PR_MEAL`/`OR_MEAL`/`WA_MEAL` with RobTime's conservative
> `NotWaivable` default, same as every other state, and the template picker doesn't need to know or
> claim anything about the underlying legal question — the client does, later, if they choose to
> loosen it (Phase 5). The calculation logic for all three was already implemented and tested
> (`PrMealPremiumRule`/`OrMealPremiumRule`/`WaMealPremiumRule` + their `StatePremiumEndToEndTests`
> cases) — only the waiver *policy* was ever in question, and that question now belongs to the
> client, not the template.

### Phase 5 — Advanced configuration
Differential rule editor (the `DayScheduleMode` modes are mutually exclusive — the form must be a
mode selector that swaps its body, not five ANDed filter sections; `PipelineContext` actively
rejects a single-day `ConsecutiveDayRange`, so the UI should too) · premium selection UI backed by
the metadata endpoint · holiday calendar management · state minimum wage table.

**Waiver-policy attestation UI (Gap I, schema from Phase 0).** Each active premium on the selection
screen shows its current waiver policy with RobTime's default pre-selected and visually distinct
from a client override. Changing it away from the default requires an explicit confirmation step
— "you are asserting this waiver is permitted in your jurisdiction; RobTime has not verified this"
— not a plain dropdown pick, so the decision is provably deliberate rather than a field someone
absent-mindedly changed. Every change is logged (`SetBy`/`SetAt`) and effective-dated, matching the
`<EffectiveDatedTimeline>` component already built in Phase 3.

### Phase 6 — Employee self-service
Punch entry · own timecard view · punch edit with audit trail surfaced · supervisor punch approval
queue.

> **Occurrence-level premium overrides moved out of this phase (decided 2026-07-27.)** The
> `SupervisorOverride`/`EmployeeWaiver` table keyed to one shift's premium — the remaining half of
> Gap I not closed by Phase 0/5's client-wide waiver *policy* — is now **Phase 8**. It's its own
> entity + endpoints + UI *and* it reaches into the engine's `WaiverEvaluator`, which makes it
> independent of the punch/timecard work rather than a screen hanging off it. Phase 6 ships a
> coherent, demoable slice sooner; Gap I stays half-open one phase longer, deliberately.

#### Three things the code doesn't have yet (found while scoping, 2026-07-27)

Each of these would otherwise surface mid-build, so they're prerequisites, not discoveries-in-waiting:

1. **No pay-period configuration exists.** The timecard below is specified per-*pay-period*, and
   `PayPeriodCalculator.ContainingDate(frequency, date, anchor)` is ready to serve it — but `PayRule`
   persists only `WorkweekStartDay`. Nothing stores which frequency a client is on, or the anchor a
   Weekly/BiWeekly cycle counts from, so "which pay period is this?" is currently unanswerable.
   **Decided 2026-07-27: it goes on `PayRule`**, beside `WorkweekStartDay` — that inherits the
   effective-dating and versioning every other rule field already has, so a client moving from
   bi-weekly to semi-monthly becomes a new `PayRule` version with a real effective date instead of an
   overwrite that silently rewrites history. (`ClientSettings` was the alternative and is the wrong
   shape: it can't express "changed as of March 1," which is exactly what a pay-period change is.)
2. **`PunchAuditEntry` is modelled and EF-mapped but never written.** `PunchService.CreateAsync`
   creates punches without one, despite the entity's own doc comment ("record of one create/edit/
   delete of a Punch") and an `Action` field whose values include `"Created"`. The whole edit/approve
   design below assumes this trail is real. Fixing it means backfilling the *create* path too, not
   just writing entries from the new edit/delete paths.
3. **`RequirePunchEditApproval` has no home.** Resolved as the `ClientSettings` row this section
   already anticipated — see the toggle bullet below.

#### Build slices

Prerequisites first; they're small and unblock everything after them.

- **6.0a — Pay-period config.** `PayPeriodFrequency` + anchor date on `PayRule`, plus migration.
- **6.0b — `ClientSettings`.** New client-wide operational-settings row holding
  `RequirePunchEditApproval` (default ON) and `ShowFullPayItemizationToEmployees` (default OFF — see
  the timecard role-gating decision below), plus migration.
- **6.0c — Make the audit trail real.** Write `PunchAuditEntry` from the existing create path, behind
  a helper the edit/delete/approve paths all share.
- **6.1 — Punch read/edit/delete.** The endpoints + service methods that don't exist yet (only
  `POST /punches` does), each writing an audit entry.
- **6.2 — `PunchChangeRequest`.** Entity + migration + submit/decide endpoints. The
  `RequirePunchEditApproval` branch *is* the feature on the API side; approval is what applies the
  change and writes the audit entry.
- **6.3 — Timecard endpoint.** `GET /employees/{id}/timecard?from=…&to=…` running `PayCalculator`.
  Update `TimeCalculation.Api.csproj`'s guard comment when it lands — it already names this as the
  next planned consumer.
- **6.4 — Self-service clock: scoping + UI.** *Not* a UI-only slice — see the scoping note below.
  Backend: the self-service scoping helper, `POST /punches` opened to Employee against their own
  record, and a new `GET /me/clock-status`. Frontend: the clock itself on `/time`, plus role-gating
  the top nav.
- **6.5 — Timecard view UI.** Header/totals · week→day→shift→pair body · line-item drill-down ·
  exceptions · edit affordance. Built **once** and mounted twice — see the first/third-person split
  below.
- **6.6 — Supervisor pending-request queue UI.**
- **6.7 — Timecard approval + lock. Done (2026-07-28).** `TimecardApproval` entity (append-only) +
  migration, approve/unapprove endpoints, locked-state gating wired into every punch-mutating path
  (`TimecardLockService`, shared by `PunchService` and `PunchChangeRequestService`). Frontend: an
  approve/unapprove action and a locked-status bar on the 6.5 timecard view, gated Supervisor+ via
  a new `can.approveTimecard`. Verified: 167 API tests, 302 engine tests, manual browser run of
  approve → frozen render → unapprove → live render again.
  Found while building, worth recording: `PayCalculationSnapshot`'s grouping graph carries
  `PunchPair.AppliedRule` (the full `PayRule`), which is both unread by the response builder and
  genuinely unserializable as stored JSON — `PayRule.ActivePremiumCodes`/`ActiveDifferentialCodes`
  are `IReadOnlySet<string>`, an interface `System.Text.Json` can't instantiate on deserialize. Fixed
  by stripping `AppliedRule` before freezing the snapshot (`TimecardService.StripAppliedRuleForSnapshot`)
  rather than adding a converter — the field was redundant with the snapshot's own top-level
  `PayRuleId`/`Name`/`Version` in the first place. Also found and fixed: `PunchEndpoints.UpdatePunch`/
  `DeletePunch` and `PunchChangeRequestEndpoints.SubmitPunchChangeRequest` had no `Conflict` branch in
  their result-mapping `switch` — a pre-existing gap (nothing before this could return `Conflict` from
  those service calls) that surfaced as a 500 instead of 409 the moment the lock check could.
  Not done, flagged rather than skipped: fuller rule lineage in the snapshot (only `PayRule` made it
  in — `DifferentialRule`/`ClientPremiumPolicy`/`HolidayCalendar`/`StateMinimumWage` ids didn't); the
  pending-request-blocks-approval edge case is period-scoped as designed, but nothing yet re-notifies
  a requester whose in-flight request is now stuck behind a locked period they can't self-resolve.
- **6.8 — Fast bulk punch entry UI. Done (2026-07-28).** A keyboard-driven grid for entering a
  stretch of punches without leaving the keyboard, covering `In`/`Out` and `FixedDollar`/
  `FixedHours`, with a live pay preview. See the design note below.
  Backend: `PunchService.CreateBatchAsync` (atomic — validates every row before writing any of
  them, checks the lock and position per row, two-phase save so audit entries land after their
  punches) behind `POST /punches/batch`; `TimecardService.PreviewAsync` merges the grid's draft
  rows with the period's already-saved punches and runs them through `PayCalculator.Calculate` —
  a running total for what the period *would* be, not the draft rows priced in isolation — behind
  `POST /employees/{id}/timecard/preview`. Preview never persists anything and deliberately isn't
  blocked by `TimecardLockService`: previewing a hypothetical change to an already-approved period
  is exactly the "what would this do" question the endpoint exists to answer.
  Reusing the Phase 4 §7 what-if path (the design note's original plan) turned out not to work:
  `PayRuleWhatIfService` only diffs pay rules against already-saved punches, it has no way to accept
  hypothetical/unsaved punches as input — caught by reading the service before building on it, so a
  new lightweight preview endpoint was written instead. Draft rows get synthetic negative ids
  (`-1`, `-2`, ...) when merged with real punches so multiple draft-only shifts don't collide on
  `Shift.AnchorPunchId`'s zero-id fallback.
  Frontend: `BulkPunchEntry` — a plain-state (not react-hook-form) row grid, since a spreadsheet-like
  grid doesn't fit a single-record form abstraction. Rows debounce into the preview endpoint
  (~400ms after typing stops) and show running Regular/OT/DT hours and gross; Enter on the last row
  appends a new one, "+ Add day" appends a ready-to-edit In/Out pair for the next day so a full week
  is a handful of keystrokes away from complete. Mounted both on `/time` (self-entry, any signed-in
  employee — the batch endpoint self-scopes the same way the single-punch route already does) and on
  People → Punches (Supervisor+, gated the same way that whole page already is — no separate
  permission was added since the existing `viewPeople`/self-scoping split already covers it).
  Position is picked once as a grid-wide default with a per-row override, not per row by default.
  Verified via 8 new backend integration tests (draft-only preview, draft+real-punch merge producing
  a correctly combined week total, preview never persisting anything, 404/400/403 scoping, and
  preview working against an already-locked/approved period); full suite (477 tests) green; frontend
  typecheck and lint clean.
  Verified live in-browser once the user signed in (this session has no Cognito credentials of its
  own, so live verification was blocked until then — same constraint 6.5's e2e suite hit). Exercised
  on both mounts (`/time` self-entry, People → Punches supervisor entry): live preview tracked typed
  rows correctly, "Add day" and Enter-to-add-row both worked, and a real `Save punches` round-tripped
  through the actual API/Postgres and correctly invalidated the `Timecard` above it, which re-rendered
  the new punch with the right position/rate with no manual refresh. Found and fixed one real bug this
  way: for an `In`/`Out` row (whose Value/Options cells are empty), Tab naturally lands on the trailing
  Remove-row button last — without a handler there, Enter on it deleted the row just filled in instead
  of adding a new one, the opposite of what every other field in the row does. Fixed by wiring the same
  Enter-to-add-row handler onto that button. Also confirmed, not fixed (a native browser constraint,
  not an app bug): a real Enter keypress while focus is inside the `datetime-local` field itself is
  swallowed by Chromium's own segmented date/time widget before it becomes a page-visible keydown, so
  Enter only advances a row from the Kind/Position/Value/Options/Remove controls, not from the When
  field — documented in `BulkPunchEntry.tsx`'s `handleRowKeyDown` rather than silently left unclear.

#### `/time` is first-person; per-employee data stays under People (decided 2026-07-28)

The screen inventory lists the timecard under `Time`, which reads as "Time is the employee's
destination and supervisors get something else there." That's the wrong cut: a supervisor reviewing
a subordinate's timecard is the *same artifact*, just someone else's — and it's the supervisor who
signs one off before payroll (§7's "Timecard approval"). Splitting `/time` by role would fork one
component into two screens for no reason.

Split by **whose data it is** instead:

| Route | Whose | Who goes there |
|---|---|---|
| `/time` | **mine** — my clock, my timecard, requests awaiting *my* approval | anyone; the clock only renders for an account with a linked `Employee` |
| `/people/$employeeId?tab=punches` | **theirs** — that employee's punches + timecard | Supervisor+ |

The People tab already exists, stubbed with "Punches and timecards land in Phase 6," and sits beside
that employee's Positions & Rates and Pay Rule tabs — per-employee data already lives there. So 6.5
builds the timecard component once and mounts it in both places, differing only in where `employeeId`
comes from (`me.employeeId` vs. the route param). The auth split falls out for free: `/time` needs
6.4's own-record scoping, while the People tab is Supervisor+ and already works against 6.3's
endpoint unchanged. 6.6's queue belongs on `/time` because it's *my* inbox — work waiting on me — not
a view of any one employee.

#### The scoping debt 6.1–6.3 deferred, paid here

"It needs no new auth — it's just an authenticated `POST /punches`" (above) is wrong, and was wrong
when written. Every endpoint the clock needs is Supervisor-or-higher today: `POST /punches`,
`GET /punches`, `GET /employees/{id}/timecard`, `POST /punch-change-requests`. Each of those slices
shipped with a comment deferring per-employee scoping to 6.4; this is where that comes due.

The mechanism is one helper, built once and reused by 6.5/6.6: resolve the caller's own `EmployeeId`
from their `AppUser` row and reject any attempt to act on a different one. **The employee id must
come from the server-side profile, never from a caller-supplied parameter** — a self-service route
that trusts a client-sent `employeeId` is strictly worse than the Supervisor-only route it replaced.
Supervisor+ keeps acting on any employee in-tenant; only the Employee role is pinned to itself.

6.4 applies it to the clock endpoints only. 6.5 and 6.6 annotate their own routes with the same
helper when they need it — small reviewable diffs rather than one broad grant to screens that don't
exist yet.

**Also in 6.4: role-gate the top nav.** `__root.tsx`'s `NAV` array is currently ungated, so all four
destinations render for every role. 6.4 is the first slice where a real Employee-role user signs in,
and they'd see People and Setup — both of which would render empty or 403. Filter it through
`lib/permissions.ts` like every other affordance.

Out of this phase by design: the **kiosk clock** (ship-gated on device/badge auth, §11) and
**notifications** (§11).

**Punch edit approval — `PunchChangeRequest` (decided 2026-07-24).** When approval is required,
editing a punch is not a direct mutation: an employee's proposed change (edit an existing punch,
delete one, or add a missed one) becomes a `PunchChangeRequest` — a new tenant-scoped (`ClientId`)
entity holding the target punch, the requested new values, the requester, a reason, `Status`
(`Pending`/`Approved`/`Denied`), and the reviewer / `ReviewedAt` / review note once decided. A
supervisor (or above) approves or denies it; **approval is what actually applies the change** to the
`Punch` and writes the existing `PunchAuditEntry`. The request row is retained regardless of outcome
— it is its own record of *who asked for what and why*, deliberately distinct from
`PunchAuditEntry`'s record of *what was actually applied* (the two aren't redundant: a denied request
produces a `PunchChangeRequest` but never a `PunchAuditEntry`, and a direct edit — approval off —
produces the audit entry with no request).

- **Configurable per client, default ON** (`RequirePunchEditApproval`). With it off, an authorized
  edit applies directly — still writing a `PunchAuditEntry`, just skipping the request/approve
  round-trip. The toggle needs a home: this is the first genuinely client-wide *operational* setting
  (`ClientPremiumPolicy` is effective-dated and premium-scoped, a poor fit), so a small
  `ClientSettings` row is probably the right call over a lone column on `Client` — but that's a
  one-line decision to make when we get here, not now.
- **Requester ≠ approver.** Employees request edits to their own punches; supervisors+ approve. A
  supervisor's/admin's own edit already holds approval authority, so it applies directly rather than
  filing a request against itself — leave room for a stricter "second approver even for supervisor
  edits" mode later, but don't build it now.
- **Only `POST /punches` exists today.** The edit / delete / submit-request / decide-request
  endpoints are Phase 6 work and must branch on the client's `RequirePunchEditApproval` setting —
  that branch is the whole feature on the API side; the entity, those endpoints, and a supervisor's
  pending-request queue are the rest.
- **Notifications are deliberately out of scope here — see §11.** A supervisor learns about pending
  requests from a queue screen; the requester sees status on their own timecard. Push notifications
  (supervisor on a new request, requester on approve/deny) are the obvious enhancement and just as
  obviously a rabbit hole (Lambda + email/in-app), so they are a tracked future item, not a Phase 6
  blocker — the synchronous flow is complete without them.

**Timecard approval and locking (decided 2026-07-28, mechanics open).** A `Timecard` isn't a stored
row — it's `PayResult` rendered for one employee over one pay period (§8, "what it is and what it
shows"). Locking it therefore means locking the underlying `Punch`es for that employee/period, not
flipping a flag on a row that doesn't exist yet, so this needs a new small entity: proposed
`TimecardApproval` (`ClientId`, `EmployeeId`, period bounds matching `PayPeriodCalculator`'s
boundaries, `ApprovedByUserId`/`ApprovedAt`, nullable `UnapprovedByUserId`/`UnapprovedAt`, and a
reference to the `PayCalculationSnapshot` frozen at approval).

**Append-only, not a mutable current-state row (decided 2026-07-28).** Current lock state is "the
latest row for (`EmployeeId`, period) has no `UnapprovedAt`"; approving again after an un-approve
writes a *new* row rather than reusing the old one. The deciding argument is the snapshot below: an
un-approve → edit → re-approve cycle produces a materially *different* pay result, and "we paid X on
the 14th, then corrected to Y on the 20th" is the record payroll actually needs — a mutable row
would overwrite exactly the history that matters. It also matches `PayCalculationSnapshot`'s own
append-only contract and the domain's existing precedent in `RetroactiveBonusRecalculator` (FLSA
§778.209), which already thinks in terms of recalculating a closed period and paying the delta.

- **Lock on approve, unlock on un-approve** — no separate "unlock" concept needed while
  approval-based locking is the only kind that exists. Approve/un-approve authority defaults to
  Supervisor+, matching decision 16 (wage visibility) and `PunchChangeRequest`'s existing
  decide-authority — flag if that's wrong.
- **Locking blocks all three punch-mutating paths**, not just direct edits: `POST /punches`,
  `PUT`/`DELETE /punches/{id}`, and submitting a new `PunchChangeRequest` against that period. A
  "signed off for payroll" period should read as frozen, not just harder to change.
- **A `Pending` `PunchChangeRequest` blocks approval (decided 2026-07-28).** Approval is refused
  while any request against that employee/period is still pending, rather than approval implicitly
  denying them — a supervisor signing off should have to actually decide the outstanding requests,
  not have them silently swept. Two consequences for 6.5/6.6: the timecard needs a "can't approve —
  N requests pending" state that links into 6.6's queue filtered to that employee/period, and the
  approve endpoint returns a 409 (not a validation error) when requests are outstanding.
- **This is the §11 "Timecard approval" item, pulled into Phase 6 scope** — but only its
  approval-triggered half. Locking a timecard purely by age (independent of approval), with an
  emergency-unlock path for that case, is a related but separate mechanism and stays deferred — see
  §11 and decision 22. A tentative default if/when it's built: auto-lock an unapproved timecard after
  ~2 pay periods have elapsed.

**Storing engine results — snapshot on approval, not an eager recalc pipeline (decided 2026-07-28).**
Raised while settling 6.7: if pay is recalculated after it has been paid out, a bug fix or small
feature change in the engine can silently change a number someone was already paid. Two designs were
considered — freeze the result when pay is approved, or always precompute (recalc on punch/pay-rule
change, store, make the timecard a dumb display). **Freeze on approval.** The eager pipeline is not
adopted, now or as a later replacement for the snapshot.

The reasoning, in the order that decided it:

- **These look like one problem but are two.** Reproducibility of paid pay is an *audit* concern:
  write-once at a business moment, never invalidated. Display/report performance is a *cache*
  concern: invalidated whenever inputs change. One mechanism serving both is either a cache that must
  never be invalidated (incoherent) or a snapshot that goes stale (wrong).
- **The lock alone does not solve this** — worth stating plainly, because it's easy to assume 6.7
  already covers it. Locking freezes the *punches*; it does nothing about the *code that interprets
  them*. Redeploy the engine and a locked period's displayed pay can still move. The snapshot is the
  actual fix, which makes it part of 6.7 rather than a follow-on.
- **The lock is what makes the snapshot correct, though.** An approved period's inputs can't change
  by construction, so the snapshot has no invalidation problem at all. Lock and snapshot are two
  halves of one feature.
- **Invalidation fan-in kills the eager pipeline.** A pay result depends on punches, `PayRule`
  version, `PayRuleAssignment`, `EmployeePositionAssignment` (rate), `Position`, `DifferentialRule`,
  `PayRule.ActiveDifferentialCodes`, `HolidayCalendar`, `ClientPremiumPolicy`, and
  `StateMinimumWage` — ten-plus invalidation edges, each of which must stay wired correctly forever,
  with every future config entity adding another. Miss one and the system serves *wrong pay* from a
  stale row, silently. Snapshot-on-approval has exactly one write trigger.
- **The snapshot gets most of the performance win anyway.** Old timecards and long-range reports are
  precisely the approved ones, so they read frozen rows and never touch `PayCalculator`. What stays
  compute-on-read is the open period — one employee, one or two periods, tens of punches through a
  pure pipeline. That is not the load problem worth building a worker queue for.
- **Deferred, not rejected:** an eager/background recalc still makes sense someday as an *optimization
  over closed data* (it's already open decision #6 in `TimeCalculation.Persistence/README.md`,
  and the calculation being pure and idempotent means any queue can drive it). It must never become
  the source of truth for paid pay — the snapshot stays authoritative either way.

**Reproducing from lineage doesn't work today, so the payload must be stored.** `PayCalculationSnapshot`
was designed to reference "the rule versions used" so a calculation could be re-run and reproduced
(`PLAN.md` §5 asserts every config entity is versioned and never mutated). That has drifted: only
`PayRule` actually carries `RuleFamilyId`/`Version` with copy-on-edit semantics. `DifferentialRule`,
`ClientPremiumPolicy`, `HolidayCalendar`, `Position`, and `EmployeePositionAssignment` are all edited
in place, so a snapshot referencing them by id alone is *not* reproducible. Storing the full
`PayResult` graph as `jsonb` (the shape the Persistence README already anticipated) is therefore
mandatory rather than an optimization — re-running is not a fallback that currently exists.

**Gaps to close in `PayCalculationSnapshot` when it's persisted** — it's a model record today with no
`DbSet`, so this is the moment to fix its shape:

- **Engine/code version stamp** — the single most load-bearing addition given what prompted this.
  The record captures *which rules* were used but not *which build interpreted them*, so after a
  pipeline bug fix there's no way to identify which snapshots came from the defective version. Cheap
  to write, impossible to reconstruct later.
- **`ClientId`** — every other tenant-scoped entity got one in Phase 0, and it's the prerequisite for
  the Postgres RLS option (§11).
- **Pay period bounds** — it has `CalculatedAt` but nothing identifying *which period* it covers, so
  it can't be looked up the way the timecard needs.
- **Fuller rule lineage** — `PayRuleVersions` + `PositionIds` predate `DifferentialRule`,
  `ClientPremiumPolicy`, `HolidayCalendar`, and `StateMinimumWage` all existing. Even unversioned,
  recording which ones applied is worth more than recording nothing.
- Keep the existing **no-PII** constraint (`EmployeeId` only) — snapshots are retained indefinitely,
  and that doc comment is deliberate.

**Payroll export reads the snapshot, never recalculates (decided 2026-07-28).** If the export re-runs
`PayCalculator`, the snapshot is decorative and the whole design buys nothing. And a genuine bug
affecting already-paid pay is fixed by calculating *forward* — a new snapshot plus a correction for
the delta — never by mutating the frozen one.

**A frozen `PayResult` alone cannot render the timecard — the snapshot must also freeze punch-level
detail (decided 2026-07-28).** Checked against the model rather than assumed, and the current shape
falls short: `PayResult` carries **no punch times whatsoever**. Its only reference to a punch is
`ShiftPay.AnchorPunchId`/`PayLineItem.AnchorPunchId` — a single int per shift. Nothing in it holds
in/out times, raw-vs-rounded, inferred `Break`/`Lunch` subtype, per-pair position, or the incomplete
(orphan In-only/Out-only) pairs the exceptions section surfaces. Rendering an approved timecard from
the snapshot as it stands would show week totals, shift gross, and line items, and silently lose the
entire punch grid in the middle — the layer §8 describes as "week → day → shift → punch pair."

**Joining live `Punch` rows back by `AnchorPunchId` does not rescue this**, which is the finding that
settles the design:

- **Shift membership is a pipeline output, not stored data.** Which punches belong to which shift is
  decided by `ShiftBuilder` (a new shift when the gap exceeds `DistanceBetweenShiftsHours`), and only
  the *anchor* survives into `PayResult`. Reconstructing the other punches of a shift means re-running
  that grouping — precisely the engine-version-dependent computation the snapshot exists to freeze.
  A join would make historical display depend on current code again, through the back door.
- **Corrections break the join.** Decision 21's append-only approvals exist so an un-approve → edit →
  re-approve cycle keeps both results. If display joins live punches, the *older* snapshot renders
  frozen pay beside since-changed times — the two halves of one screen disagreeing.
- **Soft-deleted punches** would vanish from the grid while their pay remained in the totals.

So the snapshot needs a **purpose-built frozen display projection** alongside the `PayResult`: per
shift, its pairs with in/out (raw and rounded), subtype, position id, rate, and hours, plus the
incomplete pairs. Purpose-built rather than freezing the engine's own `Shift`/`PunchPair`/`Workweek`
intermediates — snapshots are retained indefinitely, so pinning internal pipeline shapes into stored
JSON forever would make refactoring them a data-migration problem. Keep the existing no-PII rule:
reference employee and position by id and resolve names at read time, exactly as the record's doc
comment already insists. Size is not a concern worth optimizing against — a two-week period for one
employee is on the order of a few KB of `jsonb`.

**Sequencing consequence for 6.5, worth acting on before 6.7 exists.** The timecard must render
identically whether its data came from a live `PayCalculator` run (open period) or a frozen snapshot
(approved period). That means 6.5 should define the timecard's **view model as an explicit contract**
and have the live path produce it, so 6.7 only has to add a second producer. Build 6.5 directly
against `PayResult` plus ad-hoc punch queries instead, and the frozen path becomes a second rendering
implementation — the timecard gets built twice, and the two drift.

**Fast bulk punch entry (decided 2026-07-28).** Adding a week of punches one `PunchChangeRequest` or
one form submission at a time doesn't scale for a supervisor backfilling a whole crew — 6.8 is a
keyboard-driven entry grid instead: native Tab order moves between cells, Enter on the last row
appends a new one, no mouse required to enter a full week. Settled:

- **Rounding applies to manually entered punches** — `PunchRounder` runs on hand-typed times the same
  as clock punches; a typed time isn't treated as already-final.
- **Live pay preview while entering** — as built, a purpose-written `TimecardService.PreviewAsync`/
  `POST .../timecard/preview` rather than the Phase 4 §7 what-if path as originally planned here:
  `PayRuleWhatIfService` turned out to only diff pay rules against already-saved punches, with no way
  to feed it hypothetical/unsaved ones, so it couldn't do this job. The grid's rows debounce into the
  new endpoint (~400ms after typing stops) and the week's total updates as punches are typed, rather
  than saving blind.
- **Must support `FixedDollar`/`FixedHours` punch kinds**, not just `In`/`Out` — the grid needs a way
  to pick punch kind per row/entry, not just times.
- **Still open:** is position set once for the whole batch or per row? Most weeks are one position
  for one employee, but it shouldn't silently misattribute a week where it isn't. Also inherits 6.7's
  locked-period question — bulk entry against an approved/locked period should be blocked the same
  way a single edit would be.

**Time clock UI — two distinct surfaces (planned 2026-07-24).** "Time clock" is really two different
screens with different audiences, auth, and ship-gates. The plan covers the *authentication* for both
(§5); this is about the actual *screens*, which weren't spelled out.

- **Self-service clock (Phase 6, responsive down to phone).** A logged-in employee (Cognito) clocks
  in/out from their own phone or browser. It's small: a prominent Clock In / Clock Out button that
  reflects current state ("Clocked in since 8:02 AM" vs. "Clocked out"), an optional position picker
  when the employee holds more than one, and a confirmation. Lives on the employee's own timecard/home
  route. This is the "responsive for phones" case, and it needs no new auth — it's just an
  authenticated `POST /punches` from the employee's own session.
- **Shared kiosk clock (designed now, ship-gated on badge/device auth — §11).** A wall-mounted or
  counter tablet (iPad-class) that *many* employees use without logging in. Full-screen, large touch
  targets, high-contrast, glanceable from arm's length: enter or swipe a badge number → confirm the
  resolved name → Clock In / Clock Out → timed confirmation that auto-returns to the badge prompt for
  the next person. It runs on a **registered `Device`** and authenticates with the device credential
  + `(ClientId, BadgeNumber)` lookup — never Cognito, never a per-employee login (§5). Because that
  device/badge scheme is itself a deferred item (§11 "Timeclock devices + badge numbers"), the kiosk
  clock *ships* behind it — but the UI can be designed and even built against a stub auth now; it's
  the auth, not the screen, that's the blocker. This is the "responsive for iPads/tablets" case, and
  it is deliberately a *different* build from the self-service clock, not a breakpoint of it: shared
  vs. personal, no-login vs. logged-in, kiosk-locked vs. general navigation.

**Timecard — what it is and what it shows (decided 2026-07-24).** A timecard here is a **per-employee,
per-pay-period, role-gated rendering of the engine's `PayResult`** — not a new data shape. This is the
key decision: the engine already emits exactly the drillable structure a timecard wants
(`PayResult` → `WorkweekPay` → `ShiftPay` → `PayLineItem`, week → shift → line item, see `CLAUDE.md`),
so the timecard is a *display* concern over `PayCalculator`'s output, not a modelling one. Timecards
"come in many forms"; we land deliberately in the middle — richer than a raw punch log, lighter than a
post-run pay stub (a stub is money-first and post-payroll; a timecard is time-first and *pre*-payroll,
the thing a supervisor signs off before the run — see §11 "Timecard approval").

It shows, top to bottom:
- **Header / totals** — employee, pay-period range, the `PayRule` in effect, and period totals
  (regular / OT / doubletime hours, premium $, gross). Role-gated per decision 16: an employee sees
  their own hours and pay. **How much of the itemized FLSA regular-rate math an employee sees is
  client-configurable (decided 2026-07-27)** — `ClientSettings.ShowFullPayItemizationToEmployees`,
  **default OFF**. Off, an employee sees shifts, hours, their rate, gross, and premiums owed, but not
  the weighted regular-rate derivation; Supervisor+ always sees the full `PayLineItem` breakdown. On,
  the employee sees exactly what a supervisor does. The default is off because the RROP calculation
  reliably reads as "why is my rate not my rate" without context — but transparency is a legitimate
  client posture (and in some shops a bargained one), so RobTime doesn't hard-code the answer. Costs
  a second render path to test, which is the real price of making it configurable rather than picking
  one.
- **Grouped body** — week → day → shift → punch pair. Each pair shows in/out (raw and rounded when
  they differ), inferred subtype (Break/Lunch), position + rate, and hours. This is the `ShiftPay`/
  shift-and-pair structure rendered directly.
- **Line-item drill-down** — the `PayLineItem` breakdown that explains *why* each pair was paid what it
  was (regular, OT premium attribution, differentials, meal/rest premiums). The engine already
  itemizes this; the timecard is where a human finally reads it.
- **Exceptions / flags** — incomplete pairs (orphan in/out), suspected missing punches, and any
  pending `PunchChangeRequest` or recent edit, surfaced inline so the approver sees what needs
  attention before sign-off.
- **Edit affordance** — per-punch edit routes into the `PunchChangeRequest` flow above (or a direct
  edit when approval is off).

**One architectural consequence:** the timecard needs an endpoint that runs `PayCalculator` for one
employee over a date range (e.g. `GET /employees/{id}/timecard?from=…&to=…`) and returns the
`PayResult`. That's a **third** sanctioned use of the engine from the API, beyond the two the
`TimeCalculation.Api.csproj` guard comment already names (the `/metadata` read and the Phase 4
single-employee what-if) — add it to that comment when the endpoint lands, so "this project does CRUD,
not calculation" stays honestly qualified rather than quietly violated.

### Phase 7 — Full impact preview
Worker queue (open decision #6) · client-wide impact jobs · per-employee/per-shift diff drill-down ·
"what changed and why" explanation trail.

### Phase 8 — Occurrence-level premium overrides *(closes the rest of Gap I)*
Split out of Phase 6 on 2026-07-27 — see that phase's note for why. Ordering against Phase 7 is open;
they're independent, so take whichever is worth more when the time comes.

The client-wide waiver *policy* landed in Phase 0 (schema) and Phase 5 (attestation UI). This is the
other half: a `SupervisorOverride`/`EmployeeWaiver` record keyed to **one shift's premium**, for the
case where a specific occurrence is waived or overridden rather than a standing client policy being
set. Distinct from `ClientPremiumPolicy` in both grain and authority — policy is client-wide,
effective-dated, and attested by a ClientAdmin; an override is a single shift, a single premium, and
(depending on kind) a supervisor's call or an employee's own waiver.

Reaches into the engine, not just the API: `WaiverEvaluator` currently answers from policy alone, so
it grows an occurrence-level input. Expect engine tests alongside the API/UI work — that's the part
that makes this its own phase rather than a screen.

---

## 9. Decisions

Settled — say the word and I'll rework any of them.

**Mine (tell me if you disagree):**

1. **Monorepo** — `RobTimeUI/` inside the RobTime repo, not a separate repo.
2. **`openapi-typescript` + `openapi-fetch`** over NSwag/Kiota/hey-api. Types generated, client hand-written and thin.
3. ~~Separate `TimeCalculation.Identity` project rather than putting Identity in
   `TimeCalculation.Persistence`.~~ **Superseded 2026-07-23** — Cognito owns credentials now, so
   there's no local Identity package to isolate from; `AppUser` is a thin row directly on
   `PayrollDbContext` (§5).
4. **`@js-joda/core`** for dates rather than `date-fns`/`dayjs`/`Temporal`.
5. **Four roles** — SystemAdmin / ClientAdmin / Supervisor / Employee.
6. **Pay rule versioning is Phase 0, not Phase 7**, even though the feature that needs it ships last.
7. **Global query filters over per-query filtering** for tenancy — with the `== null ||` shape removed (§5).
8. **No column-level encryption now** — storage encryption + access control + audit instead, with a
   named tripwire (SSN/bank/DOB) that flips it, and a reserved table so flipping it stays cheap (§5).

**Yours (answered):**

9. ~~Cookie auth, with a documented exit path to bearer tokens.~~ **Superseded 2026-07-23 — Amazon
    Cognito + JWT bearer from Phase 1**, after pricing out Cognito for the SSO question and finding
    the free tier (10k MAU/month) removes the cost argument for deferring it (§5).
10. **Email + password for everyone** who needs web access, including employees, via Cognito.
    Timeclock + badge number is a later addition and deliberately never touches Cognito — the badge
    is a lookup key against `Employee`, not an identity assertion — tracked in §5 and §11.
11. **Template is a mandatory starting point; every field editable afterwards.** Requires template
    lineage tracking so "customised" stays visible (§6, Rule 3).
12. **True SaaS multi-tenant** — isolation is a correctness requirement, and Phase 1 ships a test
    suite that proves it (§5).
13. **No approval step** on pay rule changes; save-is-live. The `Draft` status still lands in Phase 0
    because impact preview needs it — which means approval stays cheap to add later (§11).
14. **Premium waiver policy is client-configurable, for all six premium rules** — a safe
    (`NotWaivable`) RobTime default, loosened only through an explicit, effective-dated, audited
    attestation by the client (Gap I). Supersedes the earlier "defer legal review" call — RobTime
    never needs its own answer, so PR/OR/WA templates ship in Phase 4 instead of waiting for a
    client to need them.
15. **`SystemAdmin` always scopes into one client at a time**; no cross-client aggregate view. Every
    session, `SystemAdmin` included, carries a single `_tenantClientId` — no code path with a
    partially-relaxed filter. Cross-client dashboards are a distinct future capability (§11), not a
    permission on this role.
16. **`Supervisor` sees wage rates and pay amounts.** A restricted supervisor tier is anticipated but
    not built now (§11) — `lib/permissions.ts` is the seam for it when it's needed.
17. **Punch edits require supervisor approval, configurable per client, default ON.** A
    `PunchChangeRequest` carries the proposed change through `Pending` → `Approved`/`Denied`;
    approval is what applies the change and writes the `PunchAuditEntry`. Off = direct edit, still
    audited. Notifications on request/decision are a deferred enhancement (§11), not part of the core
    synchronous flow (Phase 6).
18. **Pay-period frequency and anchor live on `PayRule`**, not on `ClientSettings` — a pay-period
    change is a dated event with payroll consequences, so it needs the effective-dating and
    versioning every other `PayRule` field already has (Phase 6.0a). Discovered missing entirely
    while scoping Phase 6 on 2026-07-27: `PayPeriodCalculator` takes frequency/anchor as parameters,
    but nothing persisted them.
19. **How much itemized regular-rate math an employee sees on their own timecard is the client's
    call** — `ClientSettings.ShowFullPayItemizationToEmployees`, default OFF (Phase 6). Same instinct
    as decision 14: where a disclosure is a legitimate matter of client posture rather than
    correctness, RobTime ships a conservative default instead of asserting the answer.
20. **`ClientSettings` is the home for client-wide *operational* settings** — flags that are neither
    effective-dated nor premium-scoped, which is what makes `ClientPremiumPolicy` a poor fit for
    them. First two occupants: `RequirePunchEditApproval` (17) and
    `ShowFullPayItemizationToEmployees` (19). Note the contrast with 18: settings land here only when
    they genuinely have no dated history worth keeping.
21. **Approving a timecard locks it for that employee/period; un-approving reopens it (Phase 6.7).**
    Pulls the §11 "Timecard approval" item into scope, but only its approval-triggered half —
    locking blocks direct edits, punch add/delete, and new `PunchChangeRequest` submissions alike.
    Mechanics (entity shape, the pending-request-at-approval-time edge case) are flagged as open in
    §8 and need answers before 6.7 is buildable.
22. **Age-based auto-lock is deferred; only a tentative default is recorded.** Locking an unapproved
    timecard once it's ~2 pay periods old, plus an emergency-unlock path for that case (distinct from
    un-approving, since there's nothing to un-approve), is the likely eventual design — noted so
    decision 21's schema doesn't quietly preclude it, not scheduled. See §11.
23. **A `Pending` `PunchChangeRequest` blocks timecard approval** — approval is refused rather than
    implicitly denying the outstanding requests. Signing off should require actually deciding them
    (Phase 6.7).
24. **Pay results are frozen into a `PayCalculationSnapshot` when a timecard is approved; an eager
    recalc-on-change pipeline is explicitly not adopted.** Locking punches doesn't stop an engine
    deploy from changing a paid period's numbers — the snapshot does. Approval is the one write
    trigger, versus ten-plus invalidation edges for an always-precomputed design, and because old
    timecards are exactly the approved ones, the snapshot also absorbs most of the read-performance
    motive. Background recalculation stays available later as an optimization over closed data
    (Persistence open decision #6) but never as the source of truth for paid pay. Full reasoning and
    the gaps to close in the existing model record are in §8.
25. **Payroll export reads the snapshot and never recalculates**, and the snapshot must therefore
    freeze **punch-level display detail alongside the `PayResult`** — the latter carries no punch
    times at all, only an `AnchorPunchId` per shift, and shift membership is a pipeline output that
    can't be reconstructed by joining live punches. Consequence for Phase 6.5: define the timecard's
    view model as an explicit contract now, so the frozen path added in 6.7 is a second producer of
    the same shape rather than a second rendering of the timecard (§8).

## 10. Follow-on questions

Both now answered (the other two — `SystemAdmin` scoping and `Supervisor` wage visibility — were
answered 2026-07-22 and moved into §5/§9). Kept here with their answers rather than deleted, so the
reasoning stays findable.

1. **Client self-signup, or do you onboard them?** — **Onboarded, not self-signup.** Settled in
   practice rather than by explicit decision: `allow_admin_create_user_only = true` on the Cognito
   pool and `AdminCreateUser` in `CognitoUserProvisioner` mean there is no registration flow and
   never was one. Recorded 2026-07-27 to stop it reading as still-open.
2. **Where do employees get their initial password?** — **Emailed invite, via Cognito itself
   (decided 2026-07-27).** No SES, no custom token, no `/accept-invite` page: `AdminCreateUser`
   already passes `DesiredDeliveryMediums = ["EMAIL"]` and never suppresses the message, so Cognito
   emails a temporary password on every provision; the Hosted UI then detects the account's
   `FORCE_CHANGE_PASSWORD` state and collects a new password before completing the OAuth redirect
   back to the SPA. The only thing that needed building was the message itself — an
   `invite_message_template` on the user pool (`infra/modules/identity/main.tf`), with the sign-in
   URL threaded in as `app_url`. A custom branded invite-link page was the alternative and was
   rejected as real added scope (SES domain verification, a public unauthenticated route, and a token
   issuance/expiry design) for a nicer email. Still becomes moot for shop-floor staff once badge auth
   lands (§11).

## 11. Future improvements

Deliberately deferred. Recorded here so the design doesn't accidentally preclude them.

| Item | Notes | Design constraint it implies today |
|---|---|---|
| **Timeclock devices + badge numbers** | Registered device + `Employee.BadgeNumber`, clock-only credential. `Punch.DeviceId`/`DevicePunchId` and the unique idempotency index already exist. The **shared kiosk clock UI is designed in Phase 6** and ship-gated on exactly this — the screen can be built against a stub, but it can't go live until device/badge auth does. | Auth must be multi-scheme-ready: authorize on policies/claims, never on cookie presence (§5). |
| **Pay rule change approval workflow** | Submit → review → activate, with the impact preview attached to the review. `PLAN.md` §9 item 14 flags the same for timecard approval. | `PayRule.Status` already has `Draft`; leave room for `PendingApproval` between `Draft` and `Active`. |
| **Punch-change-request notifications** | Supervisor notified on a new pending `PunchChangeRequest`; requester notified on approve/deny. Event-driven via Lambda (request-create / decision events → SES email first, in-app later). The synchronous Phase 6 flow works without it — a supervisor's pending-request queue is the fallback; notifications only remove the need to poll it. | Emit a domain event (or write an outbox row) on request create/decision so a Lambda can hang off it later — the request/approve write path must not inline an email send into its own transaction (same two-system-write caution as `UserProvisioningService`). |
| **Enterprise SSO (SAML/OIDC)** | Table stakes for larger SaaS customers. | Per-client auth configuration; `AppUser` must tolerate having no local password. |
| **Public / partner API** | Payroll exports, HRIS sync. | Bearer tokens + API keys as an additional scheme. |
| **Postgres RLS** | Defense-in-depth under the EF filters, not instead of them (`PLAN.md` open decision #5). | Denormalized `ClientId` on every tenant-scoped table (Phase 0) is the prerequisite either way. |
| **Column-level encryption for Tier A PII** | Triggered by SSN / bank details / DOB arriving — i.e. by W-2 or direct deposit (§5). | Tier A data lands in `employee_sensitive`, never as columns on `employees`. Per-tenant keys from day one. |
| **SOC 2 Type II** | Expect an enterprise customer to demand it before any regulator does. | Read auditing on pay data, log redaction, and key management are the controls that take longest to retrofit. |
| **Age-based timecard auto-lock + emergency unlock** | Locking an unapproved timecard once it ages past ~2 pay periods, independent of the approval-triggered lock now in scope (Phase 6.7, decision 21) — needs its own emergency-unlock path since there's no approval to remove. Explicitly out of scope until planned (decision 22). | Don't let 6.7's `TimecardApproval` shape assume approval is the only way a period becomes locked. |
| **Effective-dated `Employee.State`** | `PLAN.md` §9 item 12 — employee moves CA→NV mid-period. | Reuse `<EffectiveDatedTimeline>`; no new UI concept. |
| **Bulk employee import** | CSV onboarding for a new client. | Response DTOs and validation shapes should be reusable per-row, not just per-request. |
| **Punch import** | Bulk-load historical punches (CSV, or a prior timekeeping system's export) rather than typing them one shift at a time. Raised 2026-07-28 alongside the Phase 6.5 timecard UI design discussion — the keyboard-fast bulk-entry grid planned for that phase covers "a supervisor types a week by hand," not "load a quarter of backfill." | Should reuse `PunchService.CreateAsync`'s validation per-row rather than a separate bulk-insert path, same reusability instinct as bulk employee import above; punches created this way still need `PunchAuditEntry` writes. |
| **Punch geofencing / IP restriction** | Explicitly out of scope in `PLAN.md`. | None — device registration is the natural hook when it arrives. |
| **Cross-client dashboards/reports for `SystemAdmin`** | Aggregate metrics across all clients — explicitly wanted eventually, explicitly not a `SystemAdmin` permission today (§5). | Build as its own audited reporting path (`IgnoreQueryFilters` behind a dedicated endpoint), never as a loosened per-request tenant filter. |
| **Restricted-visibility `Supervisor` tier** | A second supervisor role that approves punches without seeing wage rates/pay amounts, alongside today's full-visibility `Supervisor` (§5). | `lib/permissions.ts` centralizes the check now specifically so this is a new role + branch later, not a scattered retrofit. |
