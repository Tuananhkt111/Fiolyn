# 0027 — Transactions record their creator; existing rows take the payer, else the account Admin

**Date:** 2026-11-10 · **Status:** Accepted (not yet built)

## Context

FRS v1.1 Appendix A.1-3 states a decision already taken: only a transaction's creator edits or
deletes it, and an Admin can edit or delete any. US-05 AC6–AC8 depend on it.

`TransactionBase` has no creator column. `PayerId` looks like one but isn't: it is nullable, it is
set to null when that user is deleted, and the caller can set it to anyone. It records who paid,
not who recorded the transaction.

## Decision

`TransactionBase` gets a non-nullable `CreatorId`, a foreign key to `AppUser` with `Restrict` on
delete. Every create path sets it to the caller:

- the leaves under `StandardTransactionServiceBase`, transfers and stock trades;
- share children, which take the creator of their parent;
- Telegram ingest, which takes the resolved sender
  ([0029](0029-telegram-ingest-and-shares-stay.md)).

The creator, or the Admin of the transaction's account
([0024](0024-roles-on-account-membership-no-workspace.md)), may edit or delete it. Whether a refused
edit answers 403 or 404 is settled when US-05 is specified.

Backfill for existing rows: `PayerId` where it is set; otherwise the Admin of the account the
transaction belongs to (the spend account, or the broker account for a stock trade).

Rejected:

- **The account Admin for every existing row:** unambiguous, but members would lose edit rights over
  their own past transactions.
- **Dropping creator-only editing:** that would undo A.1-3.

## Consequences

- A member may get edit rights over an old row they were only named on as the payer. That is
  accepted.
- `Restrict` means a user with transactions can't be hard-deleted. Users are soft-deleted already,
  so this changes nothing in practice.
- Every write path gains a creator-or-Admin check.
- Answers the High question N-3 in `FRS/README.md`.
