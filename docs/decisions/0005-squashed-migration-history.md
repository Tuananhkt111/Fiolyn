# 0005 — Migration history squashed to a single `InitialCreate`

**Date:** 2026-05-10 · **Status:** Accepted

## Context

The schema reached its current shape through roughly a dozen incremental migrations, several of
which carried hand-written SQL: a two-step nullable-then-NOT-NULL FK addition on `BrokerAccount`,
a multi-member backfill that turned single-owner spend accounts into join rows, a `Counterparty`
backfill deduplicating free-text names across two leaf tables, and the stock-to-TPT data move.

That history had value only as a path from an older production database to the current one. Its
costs were ongoing: a long `Up`/`Down` chain to reason about, hand-edited migrations that
`dotnet ef` could not regenerate, and `Down` paths that were lossy and had never been exercised.

## Decision

The entire migration history was collapsed into one migration,
`20260510023914_InitialCreate`, which creates the current schema from nothing. All prior migration
files were deleted.

## Consequences

- **The migration folder no longer records how the schema evolved.** The shape of the current
  schema is in `InitialCreate` and `AppDbContextModelSnapshot`; the story of how it got there is in
  git history and in these decision records.
- **Every migration name written in older documentation is now dangling.**
  `AddTransactionHierarchyAndMigrateStock`, `LinkBrokerAccountToSpendAccountNullable`,
  `RequireBrokerAccountSpendAccountId`, `AddSpendAccountAndBigintAmounts`,
  `SpendAccountMultiMember`, `AddCounterparty`, `LinkChildTransactionsForSharing` and
  `AddStockTransaction` do not exist as files. Neither do the backfill procedures they contained —
  those are historical and are deliberately not reproduced here.
- **Any database created before the squash cannot be migrated forward.** Applying `InitialCreate`
  to a populated pre-squash database will fail on existing tables. Such a database must either be
  rebuilt, or have its `__EFMigrationsHistory` table reconciled by hand to record `InitialCreate`
  as already applied.
- New migrations stack on top of `InitialCreate` normally. This decision is not repeatable
  cheaply — squashing again has the same one-way cost.
