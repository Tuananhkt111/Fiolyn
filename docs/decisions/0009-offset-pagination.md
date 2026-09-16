# 0009 — Offset pagination, uniform across every list endpoint

**Date:** 2026-05-10 · **Status:** Accepted

## Context

Every list endpoint returned its full table. For one household that is fine today, but a
transaction ledger grows without bound, and an unpaged `GET /api/transactions` eventually becomes a
slow query returning a response nobody wants in full.

Cursor (keyset) pagination is the better answer at scale: it has no deep-offset penalty and no
skipped-row anomaly under concurrent inserts. It also requires an opaque cursor token, an encoding
scheme, and gives up random page access — a "jump to page 5" control cannot be built on it.

## Decision

Offset pagination — `?page=1&pageSize=20`, max 100 — applied uniformly to **every** list endpoint,
user-scoped and admin alike. Every list response is wrapped in
`PagedResult<T>`: `{ items, page, pageSize, totalCount, totalPages }`.

One implementation, `PagedQueryExtensions.ToPagedResultAsync`, is the only sanctioned way to do it.
Single-item `GET /{id}` and `HEAD` endpoints stay unpaged — they return one row by definition.

Every paged query must end with an `Id` tiebreaker after its primary sort.

## Consequences

- **The `Id` tiebreaker is load-bearing, not cosmetic.** Offset slicing is only stable when the
  SQL `ORDER BY` is total. Without it, two requests can return overlapping or missing rows on
  adjacent pages whenever the primary sort has ties — same-day transactions, two accounts named
  "Cash".
- **Two round trips per request** — `LongCountAsync()` then `Skip`/`Take`. Accepted: EF ignores
  `Include` for the count, so the count query is cheap, and the post-`Where` query is passed as-is
  rather than having its includes stripped by hand.
- **Deep pages degrade.** `page=5000` makes Postgres walk the offset. Not a concern at this data
  volume; it is the cost that would eventually justify revisiting cursors.
- **Uniformity was chosen over per-endpoint judgement.** Some lists will never need paging, but a
  single response shape means no client has to ask which endpoints are wrapped.
- In-memory aggregations (the obligations rollup) use the synchronous `.ToPagedResult` overload
  rather than being forced into SQL.

See [`rules/pagination.md`](../rules/pagination.md).
