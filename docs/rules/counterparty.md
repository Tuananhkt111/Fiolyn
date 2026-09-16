# Counterparty Rules

Read this when touching `Counterparty`, `CounterpartyTransactionServiceBase`, `LendTransaction`, `BorrowingTransaction`, or `ObligationService`.

> **What it does** (contact model, name-driven contract, obligations rollup): [docs/reference/counterparties.md](../reference/counterparties.md) ·
> **Why:** [ADR 0008 — entity over free text](../decisions/0008-counterparty-entity-over-free-text.md)

## Resolve through the helper, assign the nav

`CounterpartyTransactionServiceBase` calls `CounterpartyResolver.ResolveOrCreateAsync(counterpartyRepository, name, userId)` before delegating to the standard base. Don't query `Counterparty` directly from a leaf service.

**Assign `tx.Counterparty = resolved`, never the scalar FK** — a newly staged contact has `Id == 0` until the save. See [Saving children with same-transaction parents](ef-pitfalls.md#saving-children-with-same-transaction-parents).

The resolver stages without saving, so the leaf's single `SaveAsync` commits contact + transaction together and a failed write rolls back both. Don't add a separate save.

## Short-circuit the resolver on an unchanged name

`UpdateAsync` skips the resolver round-trip when the inbound name matches the loaded `transaction.Counterparty.Name` (case-insensitive, trimmed). Keep that check — a PUT that doesn't touch the counterparty must not pay for a lookup.

**A different name repoints, it never renames.** Don't mutate `resolved.Name` to match the request; that would silently rewrite every other transaction pointing at the same contact. Renaming is `PUT /api/counterparties/{id}` only.

## Don't double-filter the soft-delete query filter

The `Counterparty` filter is `!c.IsDeleted` **only**. Never add `!c.AppUser.IsDeleted` — eager-loaded `Counterparty` navs on transactions read by SpendAccount co-members would null out when the owner is soft-deleted, and crash the response builder.

## The unique index is an expression index

Per-user case-insensitive uniqueness is `UNIQUE ("AppUserId", LOWER("Name"))`, created as raw SQL. **It does not appear in the model snapshot — that is expected.** Don't "fix" it by adding a property-based `HasIndex(...).IsUnique()`; you'd get redundant `23505`s on case-difference inserts the expression index already catches.

Conflict mapping follows the standard pattern — see [Race-safe uniqueness](ef-pitfalls.md#race-safe-uniqueness).

## Soft-delete is blocked while referenced

`CounterpartyService.EnsureNotReferencedByActiveTransactionAsync` scans lend **and** borrow before allowing a soft delete, throwing `BadRequestException`. Auto-paired share children are ordinary Lend/Borrow rows, so the same two-query scan already covers them — don't add a third source.
