# TimeCalculation.Persistence

EF Core (Npgsql + NodaTime) persistence for the payroll engine. The calculation core
(`TimeCalculation`) stays dependency-free; this project owns all persistence concerns.

## Implemented

- **`PayrollDbContext`** — code-first mapping of clients, employees, positions, punches, pay
  rules (with owned `RoundingRule`/`OvertimeRule` and a value-converted `ActivePremiumCodes` set),
  effective-dated assignment tables, punch audits, and state minimum wages.
- **Decimal precision** — money `decimal(19,4)`, hour quantities `decimal(10,4)`.
- **Indexes** — `punches(employee_id, punch_time)` (hot path), a unique
  `punches(employee_id, device_id, device_punch_id)` (device idempotency), and
  `(employee_id, effective_from)` on both assignment tables.
- **NodaTime** — `Instant → timestamptz`, `LocalDate → date` via `UseNodaTime()`.
- **Global query filters** — soft delete on punches; multi-tenant `ClientId` filter that
  short-circuits when no tenant is set (always present so the cached model is tenant-safe).
- **Idempotent ingest** — `IdempotentIngest.Deduplicate` (pure) mirrors the unique device index.
- **Retroactive bonus recalculation** — `RetroactiveBonusRecalculator` (pure, FLSA §778.209).

The EF model is validated by `PersistenceModelTests` (builds the model against the Npgsql provider
without a live database — the most that is verifiable here).

## Deferred / open decisions

- **Migrations & live schema** — no PostgreSQL is provisioned in this environment, so migrations
  are not generated here. `dotnet ef migrations add Initial` against a real database is the next step.
- **Table partitioning** — declarative partitioning of `punches` and snapshots by year is Postgres
  DDL applied in a migration, not model config.
- **Worker queue** for parallel per-`(employee, pay period)` calculation — open decision #6
  (Hangfire / MassTransit / hand-rolled `SKIP LOCKED`). The calculation is already pure and
  idempotent, so any of these can drive `PayCalculator.Calculate`.
- **Bulk ingest** — `EFCore.BulkExtensions` or raw `COPY` for backfills; the `IdempotentIngest`
  dedup runs ahead of whichever path is chosen.
- **Multi-tenancy** — open decision #5 (EF global filters, implemented here, vs Postgres RLS).
- **`PayCalculationSnapshot`** — persist as `jsonb` (the record graph is calculation output, not a
  relational aggregate).
