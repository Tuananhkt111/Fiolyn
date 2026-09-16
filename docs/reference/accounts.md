# Spend accounts & broker accounts

Where money sits, who can see it, and how the two account types link.

> **Canonical rules:** [rules/access-control.md](../rules/access-control.md) and
> [rules/transactions.md](../rules/transactions.md)

Routes, parameters and request bodies: [`openapi.json`](openapi.json) (`SpendAccount`,
`BrokerAccount` and their `Admin*` tags).

## SpendAccount

A place money sits — a wallet, a bank account, a credit card. Carries a `Name`, a `Type`
(`Cash | Credit`) and a signed `Balance` in whole VND, which may be negative.

**`Balance` is never written directly.** `PUT /api/spend-accounts/{id}` updates only `Name` and
`Type`. Every balance change flows through a transaction leaf, so the ledger and the balance can
never disagree. A manual correction is an `AdjustmentTransaction`, not a balance edit.

`Balance` carries `[ConcurrencyCheck]`, which makes concurrent writers race on the row; the loser
gets 409.

## BrokerAccount

A brokerage, always linked to the `SpendAccount` its cash moves through — `SpendAccountId` is a
required FK. A stock trade adjusts that spend account's balance automatically.

Setting or changing the link requires membership of **both** accounts. The re-check runs only when
the FK actually changed, so a rename skips it.

**Repointing a broker is not retroactive.** Historical transactions stay on the old spend account;
only future ones land on the new one.

## Membership

Both account types are shared through join entities (`SpendAccountMember`, `BrokerAccountMember`).
Membership *is* access, and every member has identical rights — see
[0011](../decisions/0011-membership-is-access.md).

- The creator becomes the first member automatically.
- Members are added by **email**, and the operation is idempotent — adding an existing member
  returns the current roster rather than failing.
- A member can remove any other member. The last member cannot be removed; delete the account
  instead.
- Non-membership is reported as **404, not 403**, so a caller learns nothing about accounts they
  cannot see.

Member-list endpoints bake the caller-membership check into the same query that fetches the data —
one round trip, not a precondition followed by a read.

There are no admin member-management endpoints. An admin who needs to act on an account adds
themselves through the normal endpoint first, which leaves a trace.

## Deletion is blocked while referenced

Soft-deleting a `SpendAccount` fails with 400 while any of these still point at it:

- an active `BrokerAccount` (`EnsureNotReferencedByActiveBrokerAsync`)
- an active transaction's `SpendAccountId`
- an active `TransferTransaction.DestinationSpendAccountId`

The FK's `Restrict` behaviour cannot enforce this, because soft delete is invisible to the
database. See [0010](../decisions/0010-broker-spend-account-fk-restrict.md).

## Admin endpoints

`/api/admin/spend-accounts` and `/api/admin/broker-accounts` mirror the user-scoped controllers
without the membership filter, and their responses carry the full member list (email + display
name). Both accept an optional `?userId=` filter to narrow to accounts a given user belongs to.

Admin creates specify the initial member explicitly via `appUserId`.

## Reading members

`GetMembersAsync` uses `.Include(a => a.Members).ThenInclude(m => m.AppUser)` rather than a
`Select` projection — a projection materializes the join rows but leaves `m.AppUser` null, and the
response DTO reads `Email` and `DisplayName` from it. Write paths that only need FK columns stay on
a projection. See [rules/ef-pitfalls.md](../rules/ef-pitfalls.md).

## Tests

`SpendAccountServiceTests`, `BrokerAccountServiceTests` under `Fiolyn.Tests/Unit/Services/`;
`SpendAccountControllerTests`, `BrokerAccountControllerTests`, `AdminSpendAccountControllerTests`,
`AdminBrokerAccountControllerTests` under `Fiolyn.Tests/Unit/Controllers/`.
