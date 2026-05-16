# Transaction Shares Rules

Read this when touching `ShareableTransactionServiceBase`, `IncomeTransaction`/`ExpenseTransaction` services, or anything involving `TransactionBase.ParentTransactionId`.

## Behavior

When an Income/Expense request carries a non-empty `shares: [{ counterpartyName, percentage }]` list, `ShareableTransactionServiceBase<T>` (`Services/Internal/BaseServices/`) splits the parent's signed `Amount` across the parent (caller's portion) and one child per share (counterparty's portion), atomically staging everything in a single `SaveAsync`.

| Parent | Child kind | Meaning |
|---|---|---|
| `IncomeTransaction` | `BorrowingTransaction` | you owe the counterparty their slice |
| `ExpenseTransaction` | `LendTransaction` | counterparty owes you their slice |

## Child wiring

Each child copies `SpendAccountId`, `Date`, `PayerId` from the parent, gets a `"Shared with {name}: {parentDescription}"` description (or `"Shared with {name}"` when the parent description is blank — the trailing colon is dropped), links back via the nullable self-referential `TransactionBase.ParentTransactionId` FK, and stays uncategorised (`Category` is null because the parent's category Kind doesn't match Lend/Borrow).

Assign `child.ParentTransaction = parent` (see `ef-pitfalls.md` "Saving children with same-transaction parents").

The leaf services (`IncomeTransactionService`, `ExpenseTransactionService`) derive from `ShareableTransactionServiceBase<TParent, TChild>` and supply the child entity type via the generic argument pair (`<IncomeTransaction, BorrowingTransaction>` / `<ExpenseTransaction, LendTransaction>`); the base constructs each child with `new TChild { ... }` thanks to the `where TChild : TransactionBase, IHasCounterparty, new()` constraint. The leaf class is reduced to a `Kind` override only — there is no `BuildChild` hook.

## Split formula

```
childAmount = sign(total) * Math.Abs(total) * percentage / 100
```

Percentage carries up to 2 decimal places. Internally `ValidateAndTrim` rounds each input to 2 decimals (half-away-from-zero) and scales to integer hundredths (`scaled = Math.Round(rounded * 100)`); the split is then `sign * (magnitude * hundredths / 10000)` so the split math and the sum check stay in exact integer arithmetic. The parent absorbs rounding loss so `parent.Amount + sum(children.Amount) == originalTotal` exactly. `SpendAccount.Balance` still moves by the full original total — once.

## Validation

- Each `percentage ∈ [0.01, 100]` after rounding to 2 decimals (`[Range(0.01, 100)]` on the request DTO + hundredths re-check in the service). Values with more precision than 2 decimals are silently rounded server-side.
- Sum (in hundredths) ≤ 10000.
- No duplicate counterparty (post-resolve by Id; for newly-staged rows whose Id is still 0, dedupe by trimmed name to catch "Bob" + "BOB" within one request).

`LinkedShareResponse.Percentage` is reconstructed from the stored child / total magnitudes and rounded to 2 decimals (`double`); approximate when the original total didn't divide evenly.

## Update is replace-all

`UpdateAsync(tx, userId, shares)` hard-deletes every linked child via the matching child repo, recomputes the split from the new request, restages children, and applies the balance delta in one save. Cross-SpendAccount move handled with `delta = newTotal − oldTotal` over both accounts where `oldTotal = oldParent.Amount + sum(oldChildren.Amount)`.

## Soft-delete cascades

Load parent with `Children` eager-loaded — the share base's `BuildReadQuery` override chains:
```csharp
.Include(t => t.Children).ThenInclude(c => (c as LendTransaction)!.Counterparty)
```
plus the same against `BorrowingTransaction`. EF Core's translator only reliably resolves cast-includes against *concrete* TPT subtypes, not interfaces — don't try to collapse the two via `IHasCounterparty`.

Reverse `parent.Amount + sum(children.Amount)` on the SpendAccount, soft-delete parent + each child via the matching child repo, save once.

Standalone Lend/Borrow rows (no `ParentTransactionId`) are unaffected.
