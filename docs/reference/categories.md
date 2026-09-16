# Transaction categories

An admin-managed lookup that tags each transaction. Every category is bound to exactly one
transaction kind.

> **Canonical rules:** the "Categories" section of
> [rules/transactions.md](../rules/transactions.md)

Routes, parameters and request bodies: [`openapi.json`](openapi.json)
(`TransactionCategory`, `AdminTransactionCategory` tags).

## Model

`TransactionCategory` carries a `Name` (max 50) and a `Kind`
(`Income | Expense | Lend | Borrowing | Transfer | Adjustment | Stock`).

- **Unique index on `(Kind, Name)`** — the same name may be reused across kinds ("Other" for both
  Income and Expense) but not twice within one kind.
- **`Kind` is immutable after create.** Only `Name` can be updated. Changing a kind would silently
  invalidate every transaction already tagged with it.
- The FK from `TransactionBase` is optional with `SetNull` on delete, so retiring a category leaves
  the transaction history intact.

## Kind matching is enforced

Each leaf transaction service rejects a `CategoryId` whose kind doesn't match, with 400, through
the shared helper:

```csharp
TransactionCategoryGuard.EnsureKindMatchesAsync( categoryRepository, categoryId, expectedKind )
```

Call it from any new leaf rather than reimplementing the projection.

## Access

Reads are open to all authenticated users — clients need the list to render a picker. Writes are
admin-only, through a separate `AdminTransactionCategoryController` at
`/api/admin/transaction-categories`, following the same split as every other resource.

Both list endpoints accept an optional `?kind=` filter and return `PagedResult<…>`.

## Tests

`TransactionCategoryServiceTests` under `Fiolyn.Tests/Unit/Services/`;
`TransactionCategoryControllerTests`, `AdminTransactionCategoryControllerTests` under
`Fiolyn.Tests/Unit/Controllers/`.
