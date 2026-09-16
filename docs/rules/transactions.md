# Transactions Rules

Read this when touching anything under `TransactionBase`, its leaves, `TransactionBalanceWriter`, `TransactionReadService`, `TransactionCategory`, or `AdjustmentTransaction`.

> **What it does** (sign table, stock totals, per-kind access, category semantics): [docs/reference/transactions.md](../reference/transactions.md) · [categories.md](../reference/categories.md) ·
> **Why:** [ADR 0006 — TPT hierarchy](../decisions/0006-tpt-transaction-hierarchy.md) · [0001 — money as `long`](../decisions/0001-money-as-long-not-decimal.md)

## Where a new leaf goes

- The simple five (Income/Expense/Lend/Borrowing/Adjustment) inherit `StandardTransactionServiceBase<T>` and override `Kind` only.
- Lend/Borrowing insert `CounterpartyTransactionServiceBase<T>` between base and leaf; Income/Expense insert `ShareableTransactionServiceBase<TParent, TChild>`.
- `TransferTransactionService` and `StockTransactionService` stand alone — two-sided membership and broker membership respectively don't fit the standard base.

Adding a leaf means a service + a user/admin controller pair. It does **not** mean a new balance-write implementation.

## Never hand-roll the balance write

Go through `Services/Internal/Helpers/TransactionBalanceWriter.SaveWithConcurrencyGuardAsync`: load the tracked `SpendAccount`(s), mutate `Balance`, single `SaveAsync`. One save keeps the row write and the balance change in one implicit transaction — don't split them, and don't add an explicit transaction wrapper.

`[ConcurrencyCheck]` on `Balance` makes concurrent writers race; the loser must surface 409 `ConflictException`, which the helper already translates.

## Never call `repository.Update(entity)`

Every `UpdateAsync` (and the `SpendAccount` PUT) mutates the **already-tracked** entity in place — loaded via `GetByIdForUserAsync` / `GetByIdAsync` — and lets EF's per-property change tracker emit the UPDATE.

`repository.Update(entity)` marks every column modified, emits a full UPDATE that clobbers concurrent balance writes, and stomps the `[ConcurrencyCheck]` `WHERE Balance = @original` predicate.

## Sign is server-side, never from the client

Request DTOs carry a positive magnitude with `[Range(1, long.MaxValue)]`; the service or controller applies the sign. `AdjustmentTransaction` is the **only** leaf whose DTO takes a signed amount and therefore skips the `[Range]` attribute — don't copy that into a new leaf.

For stock, compute at the boundary with `StockTransaction.ComputeSignedAmount` (one call = `ResolveTradeTotal` + `ComputeAmount`) rather than chaining the two yourself.

`StockTransaction.Symbol` uppercases itself in its setter — don't `ToUpperInvariant()` on assignment.

## Category kind guard

Call the shared helper from every leaf; don't reimplement the projection:

```csharp
await TransactionCategoryGuard.EnsureKindMatchesAsync( categoryRepository, categoryId, TransactionKind.Stock ) ;
```

A mismatched `CategoryId` must be 400 `BadRequestException`, never a silent ignore.

## BrokerAccount → SpendAccount FK writes

Changing `BrokerAccount.SpendAccountId` requires the caller to be a member of **both** accounts (`BrokerAccountService.EnsureUserIsSpendAccountMemberAsync`). Re-check **only when the FK actually changed** — a rename must not pay for the lookup — and refetch through the read method afterwards so the response carries the fresh nav.

Soft-delete guards live in `SpendAccountService`: `EnsureNotReferencedByActiveBrokerAsync` and `EnsureNotReferencedByActiveTransactionAsync` (which must cover `TransferTransaction.DestinationSpendAccountId`, not just `SpendAccountId`). The FK's `Restrict` cannot enforce this — soft delete is invisible to the database.
