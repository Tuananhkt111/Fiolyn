# Transaction Categories

An admin-managed lookup table that lets each transaction be tagged with a category. Every
category is bound to exactly one `TransactionKind`.

> **Canonical rules:** the "Categories" section of `.claude/rules/transactions.md`.

## Model

`TransactionCategory` rows each carry a `Name` and an immutable `Kind`
(`Income | Expense | Lend | Borrowing | Transfer | Adjustment | Stock`). `IAuditable` +
`ISoftDelete`.

- **Unique index on `(Kind, Name)`** — the same name may be reused across different kinds, but
  not twice within one kind.
- **`Kind` is immutable post-create** — only `Name` can be updated.

Each leaf transaction service rejects a `CategoryId` whose kind doesn't match the leaf with
`400 BadRequestException`, via the shared
`TransactionCategoryGuard.EnsureKindMatchesAsync(categoryRepository, categoryId, expectedKind)`
helper. Always call the guard from a new leaf rather than reimplementing the projection.

## Endpoints

### Read (all authenticated users) — `TransactionCategoryController`

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/transaction-categories?kind=…` | List active categories, optionally filtered by kind |

### Admin CRUD — `AdminTransactionCategoryController` (admin only)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/admin/transaction-categories?kind=…` | List (paginated, optional kind filter) |
| GET | `/api/admin/transaction-categories/{id}` | Fetch one |
| POST | `/api/admin/transaction-categories` | Create (`name` + `kind`) |
| PUT | `/api/admin/transaction-categories/{id}` | Rename (kind cannot change) |
| DELETE | `/api/admin/transaction-categories/{id}` | Soft-delete |

The admin/non-admin split follows the same convention as the other resources — a separate
admin controller sharing the same service interface (see `.claude/rules/access-control.md`).

Both list endpoints return `PagedResult<TransactionCategoryResponse>`.
