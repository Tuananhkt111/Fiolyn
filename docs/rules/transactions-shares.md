# Transaction Shares Rules

Read this when touching `ShareableTransactionServiceBase`, `IncomeTransaction`/`ExpenseTransaction` services, or anything involving `TransactionBase.ParentTransactionId`.

> **What it does** (split behaviour, validation limits, replace-all update, cascade): [docs/reference/transactions.md](../reference/transactions.md) ·
> **Why:** [ADR 0007 — shares as paired Lend/Borrow rows](../decisions/0007-shares-as-paired-lend-borrow-rows.md)

## The child type comes from the generic pair — there is no hook

Leaf services derive from `ShareableTransactionServiceBase<TParent, TChild>` and supply the child entity type as the second argument (`<IncomeTransaction, BorrowingTransaction>`, `<ExpenseTransaction, LendTransaction>`). The base constructs children with `new TChild { ... }` via the `where TChild : TransactionBase, IHasCounterparty, new()` constraint.

**Don't add a `BuildChild` override** — the leaf is a `Kind` override only.

## Wire children by nav, never by FK

`child.ParentTransaction = parent`, not `child.ParentTransactionId = parent.Id` — see [Saving children with same-transaction parents](ef-pitfalls.md#saving-children-with-same-transaction-parents). Same for `child.Counterparty`.

Children copy `SpendAccountId`, `Date` and `PayerId` from the parent and stay **uncategorised** — the parent's category has the wrong `Kind` for Lend/Borrow, so assigning it would fail the category guard.

## Don't delegate to `base.CreateAsync` on the share path

The share path stages parent + children + any new counterparties and calls the balance writer **once**, applying the *full* original total. Calling `base.CreateAsync(parent, userId)` from inside it would save twice and apply only the parent's portion to the balance.

Empty `shares` short-circuits to the base path — that branch is the one that may delegate.

## Keep the split in integer arithmetic

Percentages carry 2 decimal places. `ValidateAndTrim` rounds to 2 decimals (half-away-from-zero), range-checks the result against [0.01, 100], and scales to integer hundredths. That is the only range check: `TransactionShareRequest.Percentage` deliberately has no `[Range]`, which would reject values such as 0.005 before they are rounded. Scaling to hundredths means the split and the sum check stay exact:

```
childAmount = sign * ( magnitude * hundredths / 10000 )
```

**The parent absorbs rounding loss** so `parent.Amount + Σchildren.Amount == originalTotal` exactly. Don't distribute the remainder across children, and don't reach for `decimal` — see [ADR 0001](../decisions/0001-money-as-long-not-decimal.md).

Dedupe counterparties post-resolve by `Id`, **and** by trimmed name for newly staged rows whose `Id` is still `0` — otherwise "Bob" and "BOB" both slip through in one request.

## Cast-includes must target concrete TPT subtypes

`BuildReadQuery` chains two parallel `ThenInclude`s, one per concrete child type:

```csharp
.Include( t => t.Children ).ThenInclude( c => ( c as LendTransaction )!.Counterparty )
.Include( t => t.Children ).ThenInclude( c => ( c as BorrowingTransaction )!.Counterparty )
```

EF Core's translator only reliably resolves cast-includes against **concrete** TPT subtypes. Don't try to collapse the two via `IHasCounterparty` — the interface cast is not guaranteed to translate. The irrelevant cast is a no-op LEFT JOIN at runtime.

Soft-delete and update both need `Children` eager-loaded, so they must go through this query, not a bare `GetByIdAsync`.
