# Transactions

Every money-movement event, in one ledger.

> **Canonical rules:** [rules/transactions.md](../rules/transactions.md) and
> [rules/transactions-shares.md](../rules/transactions-shares.md)

Routes, parameters and request bodies: [`openapi.json`](openapi.json).
Schema and delete behaviour: [`data-model.md`](data-model.md).

## Seven kinds, one base

An abstract `TransactionBase` with seven table-per-type leaves — see
[0006](../decisions/0006-tpt-transaction-hierarchy.md). The signed `Amount` on the base is what is
applied to `SpendAccount.Balance`.

**Sign is enforced server-side per leaf.** Clients send a positive magnitude; the server decides the
direction. The exception is `Adjustment`, which is the one leaf that accepts a signed value and
therefore skips the `[Range(1, …)]` validation.

| Leaf | Stored sign |
|---|---|
| Income | + |
| Expense | − |
| Lend | − |
| Borrowing | + |
| Transfer | source −, destination + |
| Adjustment | as supplied (signed) |
| Stock | Buy −, Sell/Dividend + |

`description` is optional on every leaf. Leaving it out or sending `null` stores an empty string,
and a PUT without it clears the old one.

### Stock totals

The unsigned trade total is not a stored column — it is `Math.Abs(Amount)` on the way out.

`ResolveTradeTotal` decides what that total is. A Dividend with **both** a positive `Quantity` and
`Price` is a per-share dividend, so the total is `Quantity × Price`; anything else uses the supplied
lump total. Buy and Sell always use the supplied total, because it may include fees and so must not
be re-derived from `qty × price`.

The controller calls `StockTransaction.ComputeSignedAmount` at the boundary, which chains resolve
and sign in one call. `Symbol` uppercases itself in its setter — don't normalize it at the call
site.

`StockTransactionService` rejects a trade with a negative quantity or price, or whose signed amount
points the wrong way (a Buy that would credit, a Sell or Dividend that would debit) or is zero —
400, before anything is read or written. This covers both the API and Telegram ingest, which builds
the entity from parsed text.

## Atomic balance writes

Every mutation goes through
`TransactionBalanceWriter.SaveWithConcurrencyGuardAsync`: load the tracked `SpendAccount`(s),
mutate `Balance`, then a single `SaveAsync` that flushes the row change and the balance change
together. EF's implicit transaction makes that atomic — no explicit transaction wrapper is needed.

`SpendAccount.Balance` carries `[ConcurrencyCheck]`, so concurrent writers race and the loser
surfaces 409.

An update mutates the already-tracked entity in place, so EF emits only the changed columns
alongside the balance write. Writing one is subject to a hard prohibition — see
[rules/transactions.md](../rules/transactions.md#never-call-repositoryupdateentity).

Service split:

- Income, Expense, Lend, Borrowing and Adjustment share `StandardTransactionServiceBase<T>`.
- `TransferTransactionService` stands alone — it checks membership on both sides and deduplicates
  balance deltas before saving, since a transfer can touch four accounts at once when both ends
  change.
- `StockTransactionService` stands alone for the broker-membership rule.

## Access

- Stock writes gate on `BrokerAccount` membership.
- Everything else gates on `SpendAccount` membership.
- Transfer requires membership of **both** source and destination — on create, and on update and
  delete for both the accounts the transfer uses now and the ones it is moved to. Reads stay visible
  to a destination-only member (below); writes don't.

The unified `GET /api/transactions` reads the union: member of the row's `SpendAccount`, **or** —
for transfers — member of the `DestinationSpendAccount`, **or** — for stock rows — member of the
`BrokerAccount`. The destination branch is load-bearing: without it a user who only co-owns the
destination would never see incoming transfers.

## Shares

An Income or Expense request may carry `shares: [{ counterpartyName, percentage }]`. The amount is
split between the caller and one auto-created child per share — a `BorrowingTransaction` under an
Income parent, a `LendTransaction` under an Expense parent. See
[0007](../decisions/0007-shares-as-paired-lend-borrow-rows.md).

- `SpendAccount.Balance` moves by the **full** original total, once. The split only decides who
  owes whom.
- The parent absorbs rounding loss, so `parent.Amount + Σchildren.Amount` equals the original total
  exactly. Percentages carry 2 decimal places and are handled as integer hundredths.
- Children copy `SpendAccountId`, `Date` and `PayerId` from the parent, and stay uncategorised —
  the parent's category has the wrong `Kind` for Lend/Borrow.
- **Update is replace-all**: existing children are hard-deleted and restaged from the new request.
- **Soft-delete cascades** to children in service code, and reverses the full total.
- The stored percentage is not kept. It is reconstructed from the child and total magnitudes on
  read, so it is approximate when the total did not divide evenly.

Standalone Lend/Borrow rows (no `ParentTransactionId`) are unaffected by any of this.

## Categories

Each leaf rejects a `CategoryId` whose `Kind` doesn't match, with 400, through the shared
`TransactionCategoryGuard.EnsureKindMatchesAsync`. Call the guard from any new leaf rather than
reimplementing the projection. See [`categories.md`](categories.md).

## Adjustments

`POST /api/adjustment-transactions` is how a balance is corrected by hand. It takes a signed
`amount` plus the usual `Date` / `Description` / `Payer`, and inherits the standard atomic write.
There is no balance-edit endpoint on spend accounts.

## Tests

One `*ServiceTests` class per leaf under `Fiolyn.Tests/Unit/Services/`, plus
`TransactionReadServiceTests` for the unified read. `Integration/Api/TransactionAcceptanceTests`
drives the criteria the controller decides (sign, `[Range]`, symbol case, an omitted description)
through the real pipeline, and `Integration/Data/ConcurrencyRaceTests` races two balance writes.
