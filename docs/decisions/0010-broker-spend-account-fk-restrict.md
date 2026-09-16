# 0010 — `BrokerAccount → SpendAccount` FK is `Restrict`

**Date:** 2026-04-26 · **Status:** Accepted

## Context

A stock trade moves real cash, so a `BrokerAccount` must know which `SpendAccount` the money comes
from and returns to. That made `BrokerAccount.SpendAccountId` a required FK.

EF Core's default delete behaviour for a *required* relationship is `Cascade`. Two problems
followed:

1. **Silent data loss.** Hard-deleting a spend account would take every broker account pointing at
   it — and transitively their members — with it.
2. **EF refuses to build the model.** `SpendAccount → BrokerAccount → BrokerAccountMember →
   AppUser` combined with the other cascade paths into `AppUser` forms multiple cascade paths, which
   EF flags as a dependency loop at model-build time.

## Decision

Override to `Restrict` in `OnModelCreating` — Fluent is the only option, as there is no
`[DeleteBehavior]` data annotation:

```csharp
modelBuilder.Entity<BrokerAccount>()
  .HasOne( b => b.SpendAccount )
  .WithMany()
  .HasForeignKey( b => b.SpendAccountId )
  .OnDelete( DeleteBehavior.Restrict ) ;
```

The same reasoning applies to the other required references into `SpendAccount` and
`Counterparty`; optional links (`CategoryId`, `PayerId`) use `SetNull` so retiring a category or
removing a user does not destroy ledger history.

## Consequences

- **The database now refuses the delete instead of cascading it**, which is the safe failure.
- **`Restrict` only guards hard deletes.** Fiolyn soft-deletes, which the FK cannot see, so the
  real guard is in service code: `SpendAccountService` blocks soft-delete while any active broker
  references the account, or any active transaction references it as source or transfer
  destination.
- Changing a broker's `SpendAccountId` requires membership of **both** accounts.
- **No retroactive recompute.** Repointing a broker does not move historical balances; only future
  transactions land on the new account.

See [`rules/transactions.md`](../rules/transactions.md) and
[`../reference/data-model.md`](../reference/data-model.md) for the full delete-behaviour table.
