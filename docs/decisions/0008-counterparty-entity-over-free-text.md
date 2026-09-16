# 0008 — `Counterparty` entity replaces the free-text name

**Date:** 2026-05-07 · **Status:** Accepted

## Context

`LendTransaction` and `BorrowingTransaction` originally stored the other party as a free-text
`string CounterpartyName`. That is the cheapest possible thing that works, and it degrades badly:

- "John", "john" and "Jon " are three different people to the database.
- Correcting a name means updating every row that used it.
- There is nowhere to hang future metadata — phone, notes, a link to an `AppUser`.
- Aggregating "what does John owe me" depends on exact string equality holding across every row
  ever entered.

The obvious fix — require the client to send a `CounterpartyId` — solves the data problem but
forces a contact picker into every client before a lend can be recorded, including the Telegram
path where there is no UI at all.

## Decision

Introduce a per-`AppUser` `Counterparty` entity — a private contact list — and make
`CounterpartyId` a required FK on both leaves. The free-text column is gone.

**Keep the write contract name-driven.** Lend/borrow requests still carry `counterpartyName` as a
string. `CounterpartyResolver.ResolveOrCreateAsync` matches it case-insensitively against the
caller's existing contacts and stages a new one when there is no match, in the same `SaveAsync` as
the transaction. No client-side picker is required, and a failed transaction write rolls back the
new contact with it.

Per-user case-insensitive uniqueness is enforced by a Postgres expression index —
`UNIQUE (AppUserId, LOWER(Name))` — created as raw SQL, not modelled in EF.

## Consequences

- **Renaming happens in one place**, via `PUT /api/counterparties/{id}`. Sending a *different* name
  on a lend repoints that row to a different contact; it does not rename the original. This is
  intentional and is the behaviour that surprises people most.
- **The expression index is invisible to the model snapshot.** That is expected. Adding a
  property-based unique index alongside it would double-report conflicts.
- Uniqueness races are caught by mapping Postgres `23505` to `ConflictException`; the pre-check is
  only an optimisation for the common case.
- **The soft-delete query filter is `!IsDeleted` only.** It deliberately does *not* also gate on
  `!c.AppUser.IsDeleted` — doing so nulls out eager-loaded `Counterparty` navs on transactions read
  by spend-account co-members, crashing the response builder.
- Contacts are private per user, so there is no admin twin of `/api/counterparties` and no admin
  obligations endpoint.

See [`rules/counterparty.md`](../rules/counterparty.md).
