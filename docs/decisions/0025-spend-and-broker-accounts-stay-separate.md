# 0025 — Spend accounts and broker accounts stay separate entities

**Date:** 2026-11-10 · **Status:** Accepted

## Context

FRS v1.1 has one `Wallet` with a type of cash, credit or stock, each with its own balance. Appendix
A.2-10 asked whether "wallet (stock)" is the same concept as a broker account.

It isn't. A `SpendAccount` (cash or credit) has a balance. A `BrokerAccount` has none: its cash moves
through a linked spend account, and a trade writes that account's balance. The link is a required
foreign key, deliberately `Restrict` on delete ([0010](0010-broker-spend-account-fk-restrict.md)).

## Decision

The two entities stay. v1.1's "wallet" is a word in the interface, not a table:

- A cash or credit wallet is a `SpendAccount`.
- A stock wallet is a `BrokerAccount`, linked to one of the user's spend accounts.

So a user's first wallet (US-03 onboarding) is cash or credit. A stock wallet can be created once a
spend account exists to link it to.

Rejected: **one `Wallet` entity with its own balance.** That means redesigning how trades move cash,
reworking the `Restrict` link from 0010, and migrating existing data.

## Consequences

- The FRS needs a revision of §4.1 and US-03.
- Both membership tables get the roles from
  [0024](0024-roles-on-account-membership-no-workspace.md).
- Answers the High question A.2-10 in `FRS/README.md`.
