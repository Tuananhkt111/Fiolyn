# 0006 — One ledger: TPT hierarchy with seven leaves

**Date:** 2026-05-03 · **Status:** Accepted

## Context

Fiolyn started with stock trades as the only recorded money movement, in a standalone
`StockTransaction` table with its own balance handling. Income, expense, lending, borrowing,
transfers and manual corrections all needed recording too.

Three shapes were available:

- **Separate unrelated tables per kind.** Each kind reimplements the same audit columns, soft
  delete, category link, payer link and — critically — the atomic balance-write dance. Seven
  near-identical implementations to keep in step, and no way to answer "show me everything that
  happened last month" without a seven-way union written by hand.
- **One wide table with nullable columns.** A single table where `Symbol`, `CounterpartyId`,
  `DestinationSpendAccountId` and so on are null for most rows. Cheap to query, but nothing is
  enforceable: no FK can be required, and every consumer has to know which columns are meaningful
  for which kind.
- **Table-per-type inheritance.** Common columns in a shared base table, leaf-specific columns in
  per-leaf tables keyed on the same id.

## Decision

An abstract `TransactionBase` mapped to table `Transaction` via `UseTptMappingStrategy()`, with
seven concrete leaves: `Income`, `Expense`, `Lend`, `Borrowing`, `Transfer`, `Adjustment`, `Stock`.

The base carries the signed `Amount` that is applied to `SpendAccount.Balance` — negative is money
out, positive is money in — plus `SpendAccountId`, `Date`, `Description`, `PayerId`, `CategoryId`,
audit and soft-delete columns. Sign is enforced server-side per leaf, not trusted from the client.

Balance changes go through one place,
`TransactionBalanceWriter.SaveWithConcurrencyGuardAsync`: load the tracked `SpendAccount`, mutate
`Balance`, single `SaveAsync`.

## Consequences

- **Leaf-specific FKs stay required.** `LendTransaction.CounterpartyId` and
  `StockTransaction.BrokerAccountId` are NOT NULL because they live in tables where every row has
  them — impossible in the single-wide-table shape.
- **One unified read.** `GET /api/transactions` returns the polymorphic union, and the access
  predicate is expressed once.
- **Reads cost a join per leaf.** TPT trades query cost for integrity; at this project's data
  volume that is not a concern.
- **The id sequence is shared** across all leaves, since the base table owns it.
- Adding an eighth kind means a new leaf table, a service deriving from the appropriate base, and a
  controller pair — not a new balance-write implementation.

See [`rules/transactions.md`](../rules/transactions.md) for the sign table and
the update-path rules.
