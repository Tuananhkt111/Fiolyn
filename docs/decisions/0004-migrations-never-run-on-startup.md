# 0004 — Migrations never run on application startup

**Date:** 2026-05-03 · **Status:** Accepted

## Context

The original prototype called `db.Database.Migrate()` inside `Program.cs`, so every deploy applied
pending migrations as the app booted. That is convenient for a single-instance toy service and
became wrong as soon as the schema started carrying real data.

The failure modes:

- **Every replica races.** Each instance runs the migrator against the same database on boot.
- **A failed migration takes the app down with it**, rather than failing a deploy step that can be
  inspected and retried independently.
- **No opportunity for a manual step.** Destructive migrations — a column drop, a backfill, a
  NOT NULL tightening — need to be reviewed and sequenced against a data backfill. Startup
  migration gives nowhere to put that.
- **Schema changes become invisible.** They happen as a side effect of a deploy rather than as a
  deliberate act.

## Decision

The application never applies migrations. `Program.cs` contains no `Migrate()` and no
`EnsureCreated()` call, and neither appears anywhere in `Fiolyn/`.

Migrations are applied out-of-band, before the new image boots:

```bash
cd Fiolyn && dotnet ef database update
```

## Consequences

- **Deploying a schema change is a two-step operation.** Run the migration, then deploy. Skipping
  the first step means the new code meets an old schema and fails at query time.
- There is a window between migration and deploy where the *old* code runs against the *new*
  schema. For a column drop or rename that window is an outage. For this project's scale the
  accepted mitigation is a brief maintenance window rather than a two-phase expand/contract
  migration.
- `EnsureCreated()` is banned outright — it bypasses the migration history table entirely and
  leaves the database in a state no later migration can apply to.

See [`rules/ef-pitfalls.md`](../rules/ef-pitfalls.md) for the full migration
rules, and [`../guides/deployment.md`](../guides/deployment.md) for where this sits in the pipeline.
