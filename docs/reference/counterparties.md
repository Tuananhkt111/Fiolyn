# Counterparties & obligations

The private contact list behind lend/borrow rows, and the receivable/payable rollup over them.

> **Canonical rules:** [rules/counterparty.md](../rules/counterparty.md)

Routes, parameters and request bodies: [`openapi.json`](openapi.json) (`Counterparty`,
`Obligation` tags).

## Model

`Counterparty` is per-`AppUser` — a private contact list, not a shared directory.
`LendTransaction.CounterpartyId` and `BorrowingTransaction.CounterpartyId` are required FKs with
`Restrict` on delete. The `Name` setter trims.

Both leaves implement the marker interface `IHasCounterparty` and inherit from
`CounterpartyTransactionServiceBase<T>`, which sits between `StandardTransactionServiceBase<T>` and
the leaf service. The leaf classes are reduced to a `Kind` override.

## Names on the wire, ids in the database

The write contract stays name-driven: lend and borrow requests carry `counterpartyName` as a
string, and the service resolves it. This is deliberate — see
[0008](../decisions/0008-counterparty-entity-over-free-text.md) — so that no client needs a contact
picker before it can record a loan, including the Telegram path which has no UI at all.

`CounterpartyResolver.ResolveOrCreateAsync` matches case-insensitively against the caller's
existing contacts and stages a new one when there's no match, in the same `SaveAsync` as the
transaction. A failed transaction write therefore rolls back the new contact with it — no orphans.

The resolver assigns the **nav**, not the scalar FK (`tx.Counterparty = resolved`), because a
freshly staged contact has `Id == 0` until the save completes.

`UpdateAsync` skips the resolver round-trip entirely when the inbound name already matches the
loaded `Counterparty.Name`, so a PUT that doesn't touch the counterparty doesn't pay for the
lookup.

### Renaming vs repointing

Sending a *different* name on an existing lend **repoints** that row to a different contact. It
does **not** rename the original, which would silently rewrite every other transaction pointing at
it.

To rename a contact everywhere, use `PUT /api/counterparties/{id}`.

## Uniqueness

Per-user, case-insensitive, enforced by a Postgres expression index:

```sql
UNIQUE ("AppUserId", LOWER("Name"))
```

This is raw SQL in the migration and is **not** modelled in EF, so it will not appear in the model
snapshot. That is expected — don't add a property-based unique index alongside it. Violations
surface as 409, mapped from Postgres `23505`; the service pre-check only narrows the common case.

## Deletion is blocked while referenced

Soft-deleting a `Counterparty` fails with 400 while any active lend, borrow, or income/expense
share row still references it
(`CounterpartyService.EnsureNotReferencedByActiveTransactionAsync`).

## Co-members can read the name

Spend-account co-members can read a counterparty's *name* through the eager-loaded nav — the base's
`BuildReadQuery` chains the include, and `TransactionReadService` adds polymorphic includes for the
unified read. Only the **owner** can rename or delete the contact.

This is why the `Counterparty` soft-delete filter is `!IsDeleted` only and deliberately omits
`!c.AppUser.IsDeleted`: adding it would null out those eager-loaded navs when an owner is
soft-deleted, and crash the response builder.

## Obligations

`GET /api/obligations` aggregates, per counterparty, for the caller:

- **Receivable** — sum of `LendTransaction` magnitudes (owed to the caller)
- **Payable** — sum of `BorrowingTransaction` magnitudes (owed by the caller)
- **Net** — receivable − payable

Auto-paired share children are ordinary Lend/Borrow rows carrying a `ParentTransactionId`, so they
are already counted. There is no separate share source to reconcile — that was the point of
[0007](../decisions/0007-shares-as-paired-lend-borrow-rows.md).

Counterparties whose receivable *and* payable are both zero are filtered out.

Every query is scoped by `c.AppUserId == userId`. Because contacts are private, there is **no admin
twin** of this endpoint.

The rollup is computed in memory, so it is sorted deterministically and paged with the synchronous
`.ToPagedResult(pagination)` overload rather than being forced into SQL.

## Tests

`CounterpartyServiceTests`, `ObligationServiceTests`, `LendTransactionServiceTests`,
`BorrowingTransactionServiceTests` under `Fiolyn.Tests/Unit/Services/`;
`CounterpartyControllerTests` under `Fiolyn.Tests/Unit/Controllers/`.
