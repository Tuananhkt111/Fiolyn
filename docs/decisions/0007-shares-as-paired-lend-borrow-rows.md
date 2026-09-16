# 0007 — Income/expense shares are auto-paired Lend/Borrow rows

**Date:** 2026-05-07 · **Status:** Accepted

## Context

Real spending is frequently split: a group dinner, a household bill, a shared paycheck. Before this
change, the only way to record "30% of this expense is actually Bob's" was to manually create a
matching `LendTransaction` alongside the expense and keep the two consistent by hand.

The obvious model is a dedicated share table — `TransactionShare(TransactionId, CounterpartyId,
Percentage)` — hanging off the parent transaction.

That model has a hidden cost. A share *is* a debt. It needs a counterparty, a due date, a
settlement path, inclusion in the receivable/payable rollup, and soft-delete semantics. All of that
already exists on `LendTransaction` and `BorrowingTransaction`. A share table would need a second
implementation of each, and `GET /api/obligations` would have to aggregate over two unrelated
sources and keep them reconciled.

## Decision

No share-metadata table. A share becomes a real child transaction in the existing ledger:

| Parent | Child kind | Meaning |
|---|---|---|
| `IncomeTransaction` | `BorrowingTransaction` | you owe the counterparty their slice |
| `ExpenseTransaction` | `LendTransaction` | the counterparty owes you their slice |

Children link back through the nullable self-referential `TransactionBase.ParentTransactionId`.
Parent and children are staged in a single `SaveAsync`. The parent absorbs rounding loss so
`parent.Amount + Σchildren.Amount == the original total` exactly, while `SpendAccount.Balance`
moves by that full original total — once.

## Consequences

- **The obligations rollup needed no share-specific code.** Auto-created children are ordinary
  Lend/Borrow rows, so they were already counted. Standalone rows (no `ParentTransactionId`) are
  unaffected.
- **A share is editable as a normal transaction** — the user can set a due date on the generated
  Lend row through the regular endpoint.
- **Update is replace-all.** Changing the share list hard-deletes the existing children and
  restages from the new request, rather than diffing. Simpler and cheap at this row count.
- **Soft-deleting the parent must cascade in service code**, not at the database — the FK is
  `Restrict` and soft delete is invisible to it.
- The percentage is not stored. It is reconstructed on read from the child and total magnitudes,
  so it is approximate when the original total did not divide evenly.

See [`rules/transactions-shares.md`](../rules/transactions-shares.md) for the
split formula and validation rules.
