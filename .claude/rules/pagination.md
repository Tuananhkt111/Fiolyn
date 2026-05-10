# Pagination Rules

Read this when adding or modifying any list GET endpoint, or when working in `Fiolyn/Common/Pagination/`.

## Contract

- Every list GET endpoint takes `[FromQuery] PaginationQuery pagination` (defaults: `page=1`, `pageSize=20`, max 100) and returns `PagedResult<TResponse>` — `{ items, page, pageSize, totalCount, totalPages }`.
- Single-item GETs (`/{id}`) and HEAD endpoints stay unpaged — they return one row by definition.
- Out-of-range `Page` / `PageSize` values surface as 400 automatically via `[Range]` validation on `PaginationQuery` (driven by `[ApiController]`).

## Where to apply

- Service layer: list methods take `PaginationQuery pagination` (last positional param when there are filters like `symbol`/`userId`/`kind`) and return `Task<PagedResult<TEntity>>`. Replace the trailing `.ToListAsync()` with `.ToPagedResultAsync(pagination)` from `Fiolyn.Common.Pagination`.
- Controllers: project per-item via `page.Map(e => new TResponse(e))`. Don't hand-roll `Skip`/`Take` in services — the extension is the only sanctioned implementation.
- In-memory aggregations (e.g. `ObligationService.GetAllForUserAsync`): build the materialized list, sort it deterministically, and call `.ToPagedResult(pagination)` (the synchronous overload). Don't try to push the rollup into SQL just to use the async path.

## Stable-ordering invariant

Offset slicing is only stable when the underlying ORDER BY is deterministic. Every paged query must end with an `Id` tiebreaker after the primary sort:

- Date-descending lists (transactions): `.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)`.
- Name-ascending lists (accounts, counterparties, categories, users, allowed-emails): `.OrderBy(x => x.Name).ThenBy(x => x.Id)`.
- Members (joined-at ascending): `.OrderBy(m => m.JoinedAt).ThenBy(m => m.AppUserId)`.

Without this, two requests at different times can return overlapping or missing rows on adjacent pages — the SQL ORDER BY is non-deterministic over ties.

## Two-round-trip cost is accepted

`ToPagedResultAsync` issues `LongCountAsync()` *and* `Skip().Take().ToListAsync()`. EF ignores `Include` for the count, so the count query is cheap — pass the post-`Where`/post-`OrderBy` query as-is. Don't strip includes manually before counting.

## Tests

`PagedQueryExtensionsTests` (under `Fiolyn.Tests/Unit/Common/Pagination/`) covers the primitive — first/last/past-end pages, `Map`, `TotalPages` math. Service tests pass `new PaginationQuery()` to invoke defaults; controller tests stub the service via `Arg.Any<PaginationQuery>()` and use `PagedResultHelpers.SinglePage(...)` for terse fixtures.
