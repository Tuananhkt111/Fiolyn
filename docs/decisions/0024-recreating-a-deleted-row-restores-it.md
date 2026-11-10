# 0024 — Re-creating a soft-deleted row restores it; a deleted user stays out

**Date:** 2026-11-10 · **Status:** Accepted

## Context

Most entities are soft-deleted, and the global query filter hides deleted rows. Their unique
indexes, though, have no `IsDeleted` filter, so a deleted row still holds its unique value. The
create paths pre-checked through the filtered query, missed the deleted twin, and inserted, which
the index refused:

- re-adding a removed whitelist email, re-creating a deleted category or contact gave 409;
- naming a deleted contact on a lend, borrow or share, or a deleted user signing in again, gave 500,
  because those save paths didn't map the unique violation.

Separately, two requests staging the same new contact at once gave the loser a 500. The FRS v1.0
conformance check and the CounterpartyResolver tests found these.

## Decision

- **A create that matches a deleted row restores it.** The lookup uses `.IgnoreQueryFilters()`. An
  active match is still a 409. A deleted match is restored with `repository.Restore` (and updated:
  a whitelist entry takes the new note and inviter; an explicit contact create takes the requested
  spelling), instead of inserting. This covers `AllowedEmailService`, `TransactionCategoryService`,
  `CounterpartyService.CreateAsync` and `CounterpartyResolver`.
- **A deleted user is not restored by signing in.** `LoginWithGoogleAsync` finds a deleted user and
  answers 403, even if the address is still whitelisted. An admin's delete holds.
- **The transaction save path maps unique violations to 409**, in `TransactionBalanceWriter`, like
  every other write path.

Rejected: partial unique indexes (`WHERE NOT "IsDeleted"`). A migration would have to be applied to
production by hand before the deploy (ADR 0004), and a deleted user could then sign in as a brand-new
account. Restoring needs no schema change, and a unit test can catch it, which a database index can't.

## Consequences

- A restored row keeps its id and history. A restored contact or category reconnects to the deleted
  transactions that still reference it, which is harmless because those stay deleted.
- Renaming an active row to a name a deleted row holds is still a 409 (from the index).
- There is no way back for a deleted user through sign-in. Undeleting a user needs an admin action,
  which doesn't exist yet.
