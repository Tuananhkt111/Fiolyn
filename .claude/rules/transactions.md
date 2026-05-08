# Transactions Rules

Read this when touching anything under `TransactionBase`, its leaves, `TransactionBalanceWriter`, `TransactionReadService`, `TransactionCategory`, or `AdjustmentTransaction`.

## Model

Abstract base + seven TPT children: `IncomeTransaction`, `ExpenseTransaction`, `LendTransaction`, `BorrowingTransaction`, `TransferTransaction`, `AdjustmentTransaction`, `StockTransaction`.

The signed `Amount` on the base is what gets applied to `SpendAccount.Balance` (negative = money out, positive = money in). Sign is enforced server-side per leaf:

| Leaf | Sign |
|---|---|
| Income | + |
| Expense | − |
| Lend | − |
| Borrowing | + |
| Transfer | source − / destination + |
| Adjustment | as-supplied (signed, skips `[Range(1, …)]`) |
| Stock | via `StockTransaction.ComputeAmount` — Buy −, Sell/Dividend + |

The unsigned stock trade total is not a stored column — it's `Math.Abs(Amount)` on the way out.

## Atomic writes

Go through `Services/Internal/Helpers/TransactionBalanceWriter.SaveWithConcurrencyGuardAsync`: load the tracked `SpendAccount`(s), mutate `Balance`, single `SaveAsync`. `[ConcurrencyCheck]` on `Balance` makes concurrent writers race — loser surfaces 409 `ConflictException`.

Service split:
- The simple five (Income/Expense/Lend/Borrowing/Adjustment) share `StandardTransactionServiceBase<T>`.
- `TransferTransactionService` is its own class — two-side membership check, dedupes balance deltas before saving.
- `Stock` keeps its own service for the broker-membership rule.

## Per-kind access

- Stock writes gate on `BrokerAccount` membership.
- Everything else gates on `SpendAccount` membership.
- `Transfer` requires membership of **both** source and destination.

The unified `GET /api/transactions` (`TransactionReadService.GetAllForUserAsync`) reads the union: member of the row's `SpendAccount` OR — for `TransferTransaction` rows — member of the `DestinationSpendAccount` OR — for `StockTransaction` rows — member of the `BrokerAccount`. The destination branch is required, otherwise a user who only co-owns the destination would never see incoming transfers.

## Update path — never call `repository.Update(entity)`

Every transaction `UpdateAsync` (and `SpendAccount` PUT) mutates the already-tracked entity in-place (loaded via `GetByIdForUserAsync` / `GetByIdAsync`) and lets EF's per-property change tracker emit the UPDATE alongside the balance write in a single `SaveAsync`. The service must **not** call `repository.Update(entity)` — that marks every column modified, emits a full UPDATE that clobbers concurrent balance writes, and stomps the `[ConcurrencyCheck]` `WHERE Balance = @original` predicate.

`PUT /spend-accounts/{id}` updates only `Name`/`Type` — `Balance` is never on the PUT path; manual deltas go through `POST /api/adjustment-transactions`.

`StockTransaction.Symbol` auto-normalizes to uppercase via the setter — don't manually `ToUpperInvariant()` on assignment.

## Categories

`TransactionCategory` is admin-managed and bound to one `TransactionKind` per row (`Income | Expense | Lend | Borrowing | Transfer | Adjustment | Stock`). Unique index on `(Kind, Name)` — same name allowed across kinds. `Kind` is immutable post-create.

Each leaf service rejects mismatched `CategoryId` with 400 `BadRequestException` via the shared `TransactionCategoryGuard.EnsureKindMatchesAsync(categoryRepository, categoryId, expectedKind)` helper. Call it from every new leaf — don't reimplement the projection.

Endpoints:
- `GET /api/transaction-categories?kind=…` — all authenticated users, read-only.
- `GET/POST/PUT/DELETE /api/admin/transaction-categories?kind=…` — admin CRUD.

## Adjustments

Manual deltas go through `POST /api/adjustment-transactions` (or the admin twin). `AdjustmentTransaction` accepts a *signed* `amount` (the only leaf where it's signed and skips `[Range(1, long.MaxValue)]`), gets a `Date`/`Description`/`Payer`, and inherits the standard atomic write pattern.

## BrokerAccount → SpendAccount FK

Every `BrokerAccount` references a `SpendAccount` (NOT NULL FK). Setting/changing `BrokerAccount.SpendAccountId` requires the caller to be a member of *both* accounts (`BrokerAccountService.EnsureUserIsSpendAccountMemberAsync`).

`UpdateAsync(id, userId, name, spendAccountId)` loads the tracked entity, performs the spend-membership re-check only when the FK changed, mutates, saves, and refetches via `GetByIdForUserAsync` when the FK changed so the response carries the fresh nav. Admin overload skips the per-user check; rename-only updates skip both lookups.

Soft-deleting a `SpendAccount` is blocked while:
- any active broker references it (`EnsureNotReferencedByActiveBrokerAsync`), OR
- any active `TransactionBase` references it via `SpendAccountId` or `TransferTransaction.DestinationSpendAccountId` (`EnsureNotReferencedByActiveTransactionAsync`).
