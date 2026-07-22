# TimeCalculation.Persistence

EF Core (Npgsql + NodaTime) persistence for the payroll engine.

**Dependency:** this project references only `TimeCalculation.Model` (the pure entity/config
types) — not the `TimeCalculation` calculation engine. Persistence is a data-access concern; it
stores and retrieves entities, it doesn't run `PayCalculator`. Keeping the reference to just
`TimeCalculation.Model` means this project's dependency footprint matches what it actually does,
and the engine never has to know EF Core exists. A future API/worker project composes all three:
`TimeCalculation.Model` for the shapes, this project for storage, `TimeCalculation` to calculate.

Note: `IdempotentIngest` (dedup) and `RetroactiveBonusRecalculator` (FLSA §778.209) are pure
helpers that live in the `TimeCalculation` engine project, not here — this project's job is
mapping entities to tables, not running ingest/recalculation logic.

## Implemented

- **`PayrollDbContext`** — code-first mapping of clients, employees, positions, punches, pay
  rules (with owned `RoundingRule`/`OvertimeRule` and a value-converted `ActivePremiumCodes` set),
  effective-dated assignment tables, punch audits, and state minimum wages.
- **Decimal precision** — money `decimal(19,4)`, hour quantities `decimal(10,4)`.
- **Indexes** — `punches(employee_id, punch_time)` (hot path), a unique
  `punches(employee_id, device_id, device_punch_id)` (device idempotency), and
  `(employee_id, effective_from)` on both assignment tables.
- **NodaTime** — `Instant → timestamptz`, `LocalDate → date` via `UseNodaTime()` (configured by
  the composition root that constructs `DbContextOptions<PayrollDbContext>`).
- **Global query filters** — soft delete on punches; multi-tenant `ClientId` filter that
  short-circuits when no tenant is set (always present so the cached model is tenant-safe).

The EF model is validated by `PersistenceModelTests` (builds the model against the Npgsql provider
without a live database — the most that is verifiable here).

## Migrations

The `Initial` migration is generated and has been applied against a live PostgreSQL 18 instance.
Migrations live in `Migrations/`; the design-time context is resolved through the API project's
host, so both flags are required:

```bash
dotnet ef migrations add <Name> --project TimeCalculation.Persistence --startup-project TimeCalculation.Api
```

```bash
dotnet ef database update --project TimeCalculation.Persistence --startup-project TimeCalculation.Api
```

### Choosing an environment

`dotnet ef` boots the API's host, so it reads the same configuration chain the running app does:
`appsettings.json` → `appsettings.{Environment}.json` → environment variables. The connection
string is `ConnectionStrings:PayrollDb`.

**The tools default to `Development`**, which is why the commands above target the local database
with no extra flags. To target another environment, pass `--environment` *after* `--` (everything
after `--` goes to the app, not to `dotnet ef`):

```bash
dotnet ef database update --project TimeCalculation.Persistence --startup-project TimeCalculation.Api -- --environment Staging
```

Setting `ASPNETCORE_ENVIRONMENT` works too; the `--` form is preferable because it's explicit at the
call site and can't leak into unrelated commands in the same shell.

Deliberately, **only `appsettings.Development.json` contains a connection string** — the base
`appsettings.json` has none, so a non-local environment can never silently inherit the developer's
database. Real environments supply theirs out-of-band, which keeps credentials out of the repo:

```bash
ConnectionStrings__PayrollDb="Host=...;Database=...;Username=...;Password=..." \
  dotnet ef database update --project TimeCalculation.Persistence --startup-project TimeCalculation.Api -- --environment Production
```

(The `__` double underscore is the .NET configuration separator for `ConnectionStrings:PayrollDb`.)
For local-only secrets, `dotnet user-secrets --project TimeCalculation.Api set ConnectionStrings:PayrollDb "..."`
keeps them off disk in the repo. If the connection string is missing, startup throws naming the
environment it looked for, rather than failing later with an opaque driver error.

## Deferred / open decisions
- **Table partitioning** — declarative partitioning of `punches` and snapshots by year is Postgres
  DDL applied in a migration, not model config.
- **Worker queue** for parallel per-`(employee, pay period)` calculation — open decision #6
  (Hangfire / MassTransit / hand-rolled `SKIP LOCKED`). The calculation is already pure and
  idempotent, so any of these can drive `PayCalculator.Calculate`.
- **Bulk ingest** — `EFCore.BulkExtensions` or raw `COPY` for backfills; `IdempotentIngest.Deduplicate`
  (in the engine project) dedup runs ahead of whichever path is chosen.
- **Multi-tenancy** — open decision #5 (EF global filters, implemented here, vs Postgres RLS).
- **`PayCalculationSnapshot`** — persist as `jsonb` (the record graph is calculation output, not a
  relational aggregate).
- **Repository/unit-of-work abstraction** — deliberately not added. `IPunchRepository` and
  `IPunchAuditWriter` existed briefly as speculative interfaces with no implementation and no
  consumer; removed rather than carried forward unimplemented. If a future API project genuinely
  needs an abstraction over `PayrollDbContext` (rather than just injecting it directly), design it
  against that project's actual needs at that point.
