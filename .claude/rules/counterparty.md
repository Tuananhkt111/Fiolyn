# Counterparty Rules

Read this when touching `Counterparty`, `CounterpartyTransactionServiceBase`, `LendTransaction`, `BorrowingTransaction`, or `ObligationService`.

## Model

`Counterparty` is per-`AppUser` (private contact list). `LendTransaction.CounterpartyId` and `BorrowingTransaction.CounterpartyId` are required FKs (`OnDelete.Restrict`). The legacy free-text `CounterpartyName` no longer exists on either leaf.

Both leaves implement marker interface `IHasCounterparty` (`Entities/IHasCounterparty.cs`) and inherit from `CounterpartyTransactionServiceBase<T>`, which sits between `StandardTransactionServiceBase<T>` and the leaf service — the leaf class is reduced to a `Kind` override only.

## Resolution

The wire contract still names the field `counterpartyName` on Lend/Borrow request DTOs. `CounterpartyTransactionServiceBase.CreateAsync(tx, userId, name)` calls `CounterpartyResolver.ResolveOrCreateAsync(counterpartyRepository, name, userId)` (in `Services/Internal/Helpers/`) before delegating to the standard base — case-insensitive match against an existing row owned by the caller, otherwise stage a new one in the same `SaveAsync`.

**Assign the nav, not the FK:** `tx.Counterparty = resolved` (see `ef-pitfalls.md` "Saving children with same-transaction parents").

`UpdateAsync` short-circuits the resolver round-trip when the inbound name (case-insensitive, trimmed) matches the loaded `transaction.Counterparty.Name` — PUTs that don't touch the counterparty don't pay the lookup. Supplying a *different* name resolves to a different counterparty; the original row stays put. Use `PUT /api/counterparties/{id}` to rename in place.

## Read access for co-members

SpendAccount co-members can read the counterparty's *name* via the eager-loaded `Counterparty` nav. The base's `BuildReadQuery` chains `.Include(nameof(IHasCounterparty.Counterparty))`; `TransactionReadService.BuildQuery` adds polymorphic includes via `(t as LendTransaction)!.Counterparty`. Only the *owner* can rename or soft-delete a `Counterparty`.

## Uniqueness

Per-user case-insensitive uniqueness is enforced by a Postgres expression index `UNIQUE (AppUserId, LOWER(Name))` (raw SQL in the migration; not modelled in EF). Service maps Postgres `23505` to `ConflictException` and pre-checks for the common case.

## Soft-delete blocking

Blocked while any active lend / borrow / income-or-expense-share row references it (`CounterpartyService.EnsureNotReferencedByActiveTransactionAsync` → `BadRequestException`).

The Counterparty soft-delete query filter is `!c.IsDeleted` only — **don't** also gate on `!c.AppUser.IsDeleted`, or eager-loaded navs on transactions read by co-members null out and crash the response builder.

## Obligations endpoint

`GET /api/obligations` (`ObligationController` + `ObligationService`) aggregates per-counterparty `Receivable` (sum of `LendTransaction` magnitudes) / `Payable` (sum of `BorrowingTransaction` magnitudes) / `Net = Receivable − Payable` for the caller. Every query is scoped via `c.AppUserId == userId`. Auto-paired share children are regular Lend/Borrow rows (with `ParentTransactionId` set) so they're already counted — no separate share source. Counterparties whose receivable + payable both equal zero are filtered out. No admin twin.
