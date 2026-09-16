# 0011 — Membership is access; no owner/editor/viewer roles

**Date:** 2026-04-23 · **Status:** Accepted

## Context

Spend accounts and broker accounts started with a single `AppUserId` owner column. Real use breaks
that immediately: a household couple share one bank account, and both need to record against it.

Once an account has several users, the conventional next step is a role column on the join —
owner, editor, viewer — with a permission check per operation.

For Fiolyn's actual users that tier split has no meaning. Everyone sharing a household account
needs to read it and write to it. A role model would add a column, a check at every call site, an
answer to "what happens when the last owner leaves", and API surface to manage it — all to encode a
distinction nobody in the domain makes.

## Decision

Access is membership, full stop. `SpendAccountMember` and `BrokerAccountMember` are join entities
carrying only the two keys and a `JoinedAt` timestamp. Every member has identical rights.

User-scoped reads filter on membership, and "not a member" is reported as **404, not 403** — a
caller learns nothing about the existence of accounts they cannot see:

```csharp
.Where( r => r.Members.Any( m => m.AppUserId == userId ) )
```

Membership rows represent presence rather than mutable state, so they carry no `IAuditable` or
`ISoftDelete` — joining is an insert, leaving is a hard delete.

## Consequences

- **Any member can remove any other member**, including the one who created the account. The only
  guard is that the last member cannot be removed; delete the account instead.
- **Any member can soft-delete the whole account**, subject to the reference checks.
- **No admin member-management endpoints.** An admin who needs to act on an account adds themselves
  through the normal member endpoint first, which leaves a trace, rather than acting invisibly.
- Child resources inherit access transitively — a stock transaction is reachable by anyone who is a
  member of its broker account.
- Adding roles later means a column on the join plus a check at each call site. Nothing in the
  current shape blocks it.

See [`rules/access-control.md`](../rules/access-control.md).
