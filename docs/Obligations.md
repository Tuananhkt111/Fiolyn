# Obligations

A read-only aggregation view that rolls up the caller's lend/borrow positions per
counterparty into receivable / payable / net figures.

> **Canonical rules:** the "Obligations endpoint" section of `.claude/rules/counterparty.md`.

## What it computes

For the authenticated user, grouped by `Counterparty`:

- **Receivable** — sum of `LendTransaction` magnitudes (money others owe the caller).
- **Payable** — sum of `BorrowingTransaction` magnitudes (money the caller owes others).
- **Net** — `Receivable − Payable`.

Auto-paired share children (Lend/Borrow rows carrying a `ParentTransactionId`, created by the
income/expense share feature) are ordinary lend/borrow rows, so they are already included —
there is no separate share source. See `.claude/rules/transactions-shares.md`.

## Scoping & filtering

- Every query is scoped by `c.AppUserId == userId` — counterparties are per-user private
  contact lists, so there is **no admin twin** of this endpoint.
- Counterparties whose receivable **and** payable both equal zero are filtered out of the
  result.

## Endpoint

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/obligations` | authenticated | Per-counterparty receivable/payable/net for the caller |

Returns `PagedResult<ObligationResponse>`. Because the rollup is an in-memory aggregation, the
service builds the materialized list, sorts it deterministically, and pages it with the
synchronous `.ToPagedResult(pagination)` overload (see `.claude/rules/pagination.md`).

## Implementation

`ObligationController` → `ObligationService.GetAllForUserAsync(userId, pagination)`. Response
DTO: `Controllers/DTOs/Responses/Obligation/ObligationResponse.cs`.
