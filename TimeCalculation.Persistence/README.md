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
Migrations live in `Migrations/`.

This project is self-sufficient for migrations — it needs **no startup project**. The schema belongs
to Persistence, so Persistence describes it without reaching through the API.
`PayrollDbContextFactory` (an `IDesignTimeDbContextFactory`) supplies the design-time context, and
`dotnet ef` prefers a factory over booting a host. Run from this directory:

```bash
cd TimeCalculation.Persistence
dotnet ef migrations add <Name>
dotnet ef database update
```

### Choosing the target database

The factory resolves the connection string in this order:

1. `ROBTIME_PAYROLL_DB` — the knob for any non-local target
2. `ConnectionStrings__PayrollDb` — the same variable the API honours, convenient in a shared shell
3. the local development database — so everyday local work stays zero-config

```bash
ROBTIME_PAYROLL_DB="Host=...;Database=...;Username=...;Password=..." dotnet ef database update
```

Naming the database is deliberately explicit rather than inheriting an ambient environment name: a
migration alters schema, and it should be obvious at the call site what it is about to alter.
Defaulting to local is the safe failure mode — forgetting the variable migrates your own machine,
never someone else's environment. Credentials for real environments therefore never live in the repo.

### How the running API resolves its connection string

Separately from migrations, the API uses the normal ASP.NET Core configuration chain:
`appsettings.json` → `appsettings.{Environment}.json` → environment variables. Only
`appsettings.Development.json` carries a connection string, so a deployed environment cannot
silently inherit a developer's database — it must supply `ConnectionStrings__PayrollDb` itself (the
`__` is .NET's separator for `ConnectionStrings:PayrollDb`). If it is missing, startup throws naming
the environment it looked in. For local-only secrets:
`dotnet user-secrets --project TimeCalculation.Api set ConnectionStrings:PayrollDb "..."`.

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
