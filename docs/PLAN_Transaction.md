# Plan: Generalized Transaction Hierarchy

## Context

Every money-movement event in Fiolyn — stock trades, expenses, transfers, lend/borrow, manual adjustments — flows through one ledger so the same atomic balance update + audit trail applies everywhere. The shape is an abstract `TransactionBase` with seven concrete TPT children. The legacy
`POST /spend-accounts/{id}/balance-adjustments` endpoint is replaced by `POST /api/adjustment-transactions`.

---

## 1. Hierarchy

```
TransactionBase (abstract, table "Transaction")
├── IncomeTransaction        — money in
├── ExpenseTransaction       — money out
├── LendTransaction          — money out + counterparty + due date
├── BorrowingTransaction     — money in + counterparty + due date
├── TransferTransaction      — money out of source + into destination
├── AdjustmentTransaction    — manual signed delta
└── StockTransaction         — Buy/Sell/Dividend + symbol/quantity/price/broker
```

EF Core mapping: `UseTptMappingStrategy()`. Each leaf has its own table sharing the `Transaction.Id` primary key.

### Base columns

| Column | Type | Notes |
|---|---|---|
| `Id` | `bigint` | PK, auto-increment, **shared** across leaves |
| `SpendAccountId` | `bigint` | FK → `SpendAccount`. Source for `Transfer`. |
| `CategoryId` | `bigint?` | FK → `TransactionCategory`. Optional. |
| `Date` | `timestamptz` | UTC date of the event |
| `Description` | `varchar(500)` | Optional |
| `PayerId` | `bigint?` | FK → `AppUser`. Defaults to caller. |
| `Amount` | `bigint` | **Signed** — negative = money out, positive = money in |
| `Created`, `LastModified`, `IsDeleted` | | `IAuditable` + `ISoftDelete` |

### Sign rule (server-side, applied by the controller)

| Leaf | Client sends | Stored `Amount` |
|---|---|---|
| `IncomeTransaction` | positive `amount` | `+amount` |
| `ExpenseTransaction` | positive `amount` | `-amount` |
| `LendTransaction` | positive `amount` | `-amount` |
| `BorrowingTransaction` | positive `amount` | `+amount` |
| `TransferTransaction` | positive `amount` | source: `-amount`, destination: `+amount` |
| `AdjustmentTransaction` | signed `delta` | `delta` |
| `StockTransaction` | Buy/Sell/Dividend + `totalAmount` | Buy → `-totalAmount`, Sell/Dividend → `+totalAmount` |

---

## 2. `TransactionCategory`

Admin-managed lookup, bound to one `TransactionKind` per row. Unique index on `(Kind, Name)` so the same name is allowed across kinds (e.g. "Other" for both Income and Expense). `Kind` is immutable post-create.

`TransactionKind` enum: `Income | Expense | Lend | Borrowing | Transfer | Adjustment | Stock`.

Each leaf service validates that any supplied `CategoryId` has a matching `Kind` — mismatch → 400 `BadRequestException`. The check is centralized in `Services/Internal/Helpers/TransactionCategoryGuard.EnsureKindMatchesAsync(categoryRepository, categoryId, expectedKind)`; every leaf service calls it rather than reimplementing the projection.

---

## 3. Endpoints

| User | Admin | Notes |
|---|---|---|
| `POST/GET/PUT/DELETE /api/income-transactions` | `/api/admin/income-transactions` | |
| `POST/GET/PUT/DELETE /api/expense-transactions` | `/api/admin/expense-transactions` | |
| `POST/GET/PUT/DELETE /api/lend-transactions` | `/api/admin/lend-transactions` | |
| `POST/GET/PUT/DELETE /api/borrowing-transactions` | `/api/admin/borrowing-transactions` | |
| `POST/GET/PUT/DELETE /api/transfer-transactions` | `/api/admin/transfer-transactions` | |
| `POST/GET/PUT/DELETE /api/adjustment-transactions` | `/api/admin/adjustment-transactions` | Replaces `/balance-adjustments` |
| `POST/GET/PUT/DELETE /api/stock-transactions` | `/api/admin/stock-transactions` | Pre-existing |
| `GET /api/transactions` | `GET /api/admin/transactions` | Polymorphic union |
| `GET /api/transaction-categories?kind=…` | `GET/POST/PUT/DELETE /api/admin/transaction-categories?kind=…` | Read available to all authenticated users; write is admin-only |

Removed: `POST /api/spend-accounts/{id}/balance-adjustments` (and admin twin).

---

## 4. Access control

- **Stock**: caller must be a member of the `BrokerAccount`. Preserved from before.
- **Every other leaf**: caller must be a member of the `SpendAccount`. `Transfer` requires membership of **both** source and destination on writes.
- **Unified `GET /api/transactions`** (`TransactionReadService`): predicate is the union of "member of source `SpendAccount`" OR — for `TransferTransaction` rows — "member of `DestinationSpendAccount`" OR — for `StockTransaction` rows — "member of `BrokerAccount`". Destination-only members must still see incoming transfers.

`SpendAccount` soft-delete is now blocked while any active `TransactionBase` references it via `SpendAccountId` or via `TransferTransaction.DestinationSpendAccountId`.

---

## 5. Atomic balance writes

`Services/Internal/Helpers/TransactionBalanceWriter.SaveWithConcurrencyGuardAsync<T>(repo, params spendAccountIds)` wraps `repo.SaveAsync()` in `try/catch (DbUpdateConcurrencyException) → ConflictException`. Every leaf's Create/Update/Delete:

1. Loads the affected `SpendAccount`(s) (tracked).
2. Mutates `Balance`.
3. Stages the row write (insert/soft-delete or in-memory mutation for update).
4. Calls the helper exactly once.

`SpendAccount.Balance` carries `[ConcurrencyCheck]`, so concurrent writers race for the row — the loser surfaces a 409.

For `Update` and `Transfer`, the same single-`SaveAsync` covers the cross-spend reverse-old / apply-new pattern (Transfer can touch four accounts at once if source and destination both change; the service deduplicates deltas before saving).

---

## 6. Migration

`AddTransactionHierarchyAndMigrateStock`:

1. Create `TransactionCategory` + unique index on `(Kind, Name)`.
2. Create `Transaction` (base) with FKs to `SpendAccount` (Restrict), `TransactionCategory` (SetNull), `AppUser` (SetNull for `PayerId`).
3. Backfill `Transaction` rows from existing `StockTransaction` data; `Amount` is `-TotalAmount` for Buy, `+TotalAmount` for Sell/Dividend; `PayerId` is NULL on legacy rows.
4. Drop `StockTransaction`'s now-base columns plus the redundant `TotalAmount` (derivable as `Math.Abs(Amount)`): `TransactionDate`, `Notes`, `TotalAmount`, `Created`, `LastModified`, `IsDeleted`.
5. Rename `StockTransaction.TransactionType` column → `StockTransactionType`.
6. Drop the auto-increment identity on `StockTransaction.Id` (base owns it under TPT) and add the FK `StockTransaction.Id → Transaction.Id`.
7. Create the six new leaf tables.
8. Bump the `Transaction` identity sequence past the migrated stock ids.

---

## 7. Files summary

**New entities:** `TransactionBase`, `TransactionCategory`, `IncomeTransaction`, `ExpenseTransaction`, `LendTransaction`, `BorrowingTransaction`, `TransferTransaction`, `AdjustmentTransaction`.

**New enum:** `TransactionKind`. **Renamed enum:** `TransactionType` → `StockTransactionType`.

**New services:** one per leaf (`IncomeTransactionService`, `ExpenseTransactionService`, `LendTransactionService`, `BorrowingTransactionService`, `TransferTransactionService`, `AdjustmentTransactionService`) plus `TransactionCategoryService` and `TransactionReadService`. The simple five share a `StandardTransactionServiceBase<T>` in `Services/Internal/BaseServices/`.

**New controllers:** one user + one admin per leaf, plus `TransactionController` (unified GET), `TransactionCategoryController` (read-only, all authenticated users), and `AdminTransactionCategoryController`.

**New DTOs:** under `Controllers/DTOs/{Requests,Responses}/{Leaf}/`. Polymorphic `TransactionResponse` for the unified GET.

**Modified:** `StockTransaction` (now `: TransactionBase`), `StockTransactionService`, `SpendAccountService` (`AdjustBalance*` removed; transaction-reference soft-delete check added), `TelegramMessageService` (sets `PayerId`), Stock DTOs (renames), `SpendAccountController` / `AdminSpendAccountController` (balance-adjustment routes removed), `AppDbContext` (TPT mapping + filters + FKs), `ScopedBusinessServicesRegister`.

**Deleted:** `SpendAccountBalanceAdjustmentRequest`, `SpendAccountBalanceResponse` DTOs.

---

## 8. Verification

1. `dotnet build` — no errors.
2. `dotnet ef database update` — migration applies cleanly; `SELECT COUNT(*) FROM "Transaction"` matches `SELECT COUNT(*) FROM "StockTransaction"`; signed `Amount` looks right on a Buy and a Sell row.
3. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — all green.
4. End-to-end: create an `Adjustment`, an `Expense`, a `Transfer`, a `Lend` and a `Stock` row through their endpoints; verify the `SpendAccount.Balance` is correct after each mutation; `GET /api/transactions` returns all rows with correct `kind` and per-leaf detail blocks.
5. Confirm `POST /spend-accounts/{id}/balance-adjustments` returns 404.
