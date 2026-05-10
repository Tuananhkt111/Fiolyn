# Offset-Based Pagination for All List GET Endpoints

## Context

Every list GET endpoint in Fiolyn currently returns the full table (`.ToListAsync()` with no limit). For a single user this is fine today, but for transactions on a long-running account it grows unbounded — one bad query can drag a page render or hammer the DB. We're adding offset-based pagination to all 31 list endpoints uniformly so client UX, response sizes, and DB pressure all stay predictable.

**User-confirmed decisions:**

1. **Style:** offset-based (`?page=…&pageSize=…`). Cursor-based was rejected as overkill for current scale.
2. **Coverage:** all 31 list endpoints (15 user-scoped + 16 admin) get the same `PagedResult<T>` wrapper. Predictable client contract, no mixed shapes.
3. **Defaults:** `page=1`, `pageSize=20`, `maxPageSize=100`. Mobile-friendly default, hard cap to prevent accidental full-table dumps.
4. **Response shape:** body wrapped in `PagedResult<T>` — `{ items, page, pageSize, totalCount, totalPages }`. This is a breaking change to every list response.

**Out of scope:**

- Single-item GETs (`/{id}`) and HEAD endpoints — they return one row by definition.
- Filtering / searching / sorting controls — keep existing sort orders untouched (transactions by `Date` desc, others by `Name` asc, members by `JoinedAt` asc). Adding query filters is a separate ticket.

---

## Design

### 1. New shared types (`Fiolyn/Common/Pagination/`)

One file per type.

- **`PaginationQuery.cs`** — request DTO consumed by `[FromQuery]`:
  ```csharp
  public record PaginationQuery
  {
    public const int DefaultPageSize = 20 ;
    public const int MaxPageSize     = 100 ;

    [Range( 1, int.MaxValue )] public int Page     { get ; init ; } = 1 ;
    [Range( 1, MaxPageSize )]  public int PageSize { get ; init ; } = DefaultPageSize ;
  }
  ```
  `[ApiController]` already wires up automatic 400 on invalid model state, so out-of-range values surface without controller boilerplate.

- **`PagedResult.cs`** — response wrapper record:
  ```csharp
  public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount )
  {
    public int TotalPages => PageSize == 0 ? 0 : ( int )Math.Ceiling( TotalCount / ( double )PageSize ) ;
  }
  ```
  `TotalCount` is `long` to match VND-money-precedent and `LongCountAsync`. The CamelCase contract resolver (`WebApiServiceRegister.cs:20`) emits `items, page, pageSize, totalCount, totalPages`.

- **`PagedQueryExtensions.cs`** — single extension that powers every list call:
  ```csharp
  public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
    this IQueryable<T> query, PaginationQuery pagination )
  {
    var total = await query.LongCountAsync() ;
    var items = await query
      .Skip( ( pagination.Page - 1 ) * pagination.PageSize )
      .Take( pagination.PageSize )
      .ToListAsync() ;
    return new PagedResult<T>( items, pagination.Page, pagination.PageSize, total ) ;
  }

  public static PagedResult<TOut> Map<TIn, TOut>(
    this PagedResult<TIn> source, Func<TIn, TOut> selector ) =>
    new( source.Items.Select( selector ).ToList(), source.Page, source.PageSize, source.TotalCount ) ;
  ```
  Two DB round trips per request (count + page). EF ignores `Include` for the count, so we pass the post-`Where`/post-`OrderBy` query as-is — no manual include stripping.

### 2. Stable ordering — Id tiebreaker

Offset pagination is only stable when `OrderBy` is deterministic. Current sorts have ties (two SpendAccounts named "Cash" across users in admin view; same-day transactions). Append `.ThenBy(x => x.Id)` (or `.ThenByDescending(x => x.Id)` when the primary sort is descending) at every list-query site. Concretely:

- `StandardTransactionServiceBase.BuildReadQuery` — append `.ThenByDescending(t => t.Id)`.
- `TransactionReadService.BuildQuery` — same.
- All other list call sites — append `.ThenBy(x => x.Id)` after the existing `OrderBy`.

### 3. Service layer — `Task<List<T>>` → `Task<PagedResult<T>>`

For every list method, change the return type and add `PaginationQuery query` as the trailing parameter. Update the implementation **and** the matching interface. Replace the trailing `.ToListAsync()` (or list-returning projection) with `.ToPagedResultAsync(query)`.

| Service | File | Methods to update |
|---|---|---|
| `StandardTransactionServiceBase<T>` (covers Income/Expense/Lend/Borrowing/Adjustment via inheritance) | `Fiolyn/Services/Internal/BaseServices/StandardTransactionServiceBase.cs` | `GetAllForUserAsync`, `GetAllAsync` |
| `TransactionReadService` | `Fiolyn/Services/Internal/TransactionReadService.cs` | both list methods |
| `SpendAccountService` | `Fiolyn/Services/Internal/SpendAccountService.cs` | `GetAllForUserAsync`, `GetAllAsync(userId?)`, `GetMembersAsync` |
| `BrokerAccountService` | `Fiolyn/Services/Internal/BrokerAccountService.cs` | `GetAllForUserAsync`, `GetAllAsync(userId?)`, `GetMembersAsync` |
| `StockTransactionService` | `Fiolyn/Services/Internal/StockTransactionService.cs` | `GetAllForUserAsync(userId, symbol?, query)`, `GetAllAsync(userId?, symbol?, query)` — preserve `symbol` |
| `TransferTransactionService` | `Fiolyn/Services/Internal/TransferTransactionService.cs` | `GetAllForUserAsync`, `GetAllAsync` |
| `CounterpartyService` | `Fiolyn/Services/Internal/CounterpartyService.cs` | `GetAllForUserAsync` |
| `ObligationService` | `Fiolyn/Services/Internal/ObligationService.cs` | `GetAllForUserAsync` — paginate the in-memory aggregated list (already materialized; wrap directly). Order by `Name` asc with `Id` tiebreaker before slicing. |
| `TransactionCategoryService` | `Fiolyn/Services/Internal/TransactionCategoryService.cs` | `GetAllAsync(kind?)` |
| `AllowedEmailService` | `Fiolyn/Services/Internal/AllowedEmailService.cs` | `GetAllAsync` |
| `UserService` | `Fiolyn/Services/Internal/UserService.cs` | `GetAllAsync` |

**`AddMemberAsync` on Spend / Broker** — these write methods return the post-add roster (`List<*Member>`). They keep returning the **full** list (not paged) — the response is "here is everything after your write". Extract the existing `LoadMembersAsync` private helper as the non-paged path; the public `GetMembersAsync` becomes the paged variant. `RemoveMemberAsync` returns `Task` and is unchanged.

### 4. Controllers — `[FromQuery] PaginationQuery query`, return `PagedResult<TResponse>`

Pattern (replaces every `[HttpGet]` list method):

```csharp
[HttpGet]
public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery query )
{
  var page = await service.GetAllForUserAsync( User.GetUserId(), query ) ;
  return Ok( page.Map( e => new IncomeTransactionResponse( e ) ) ) ;
}
```

For `TransactionController` use `page.Map( TransactionResponse.FromEntity )`. Controllers with extra query params (e.g. `StockTransactionController` accepts `?symbol=…`) keep that param alongside `PaginationQuery`. **31 controller methods total** — same trivial transform across all of them.

### 5. Tests

**Update existing list tests.** Assertions that previously did `result.Should().HaveCount(N)` now do `result.Items.Should().HaveCount(N)` and add `result.TotalCount.Should().Be(N)`. Service-layer tests pass a fresh `new PaginationQuery()` to invoke the default page.

**Add focused pagination tests** in `Fiolyn.Tests/Unit/Common/Pagination/PagedQueryExtensionsTests.cs`:

- `Page=1, PageSize=2` over a 5-row source — `Items.Count == 2`, `TotalCount == 5`, `TotalPages == 3`.
- `Page=3, PageSize=2` over 5 rows — last item only.
- `Page=10` past the end — empty `Items`, correct metadata.
- `Map` projects items but preserves page metadata.

Plus **one** end-to-end pagination test per service family — `TransactionReadService` (covers polymorphic includes + count), `SpendAccountService`, `StockTransactionService` (symbol + page combo), `ObligationService` (in-memory aggregation path). Don't duplicate per-leaf — the base-class test covers the five standard leaves.

`MaxPageSize` enforcement is exercised through one controller test that posts `?pageSize=999` and expects 400.

### 6. Documentation updates

Pagination touches several rule files — current text describes the old `Task<List<T>>` shape, ordering, and `.AsNoTracking()` placement, all of which change. Update in lock-step with the code:

- **New `.claude/rules/pagination.md`** — single source of truth for the pattern (contract, where to apply, implementation pointer, stable-ordering invariant, in-memory aggregation note, two-round-trip tradeoff).
- **`.claude/CLAUDE.md`** — pointer line to `pagination.md` alongside the other rule-file links.
- **`.claude/rules/access-control.md`** — update the inline `GetAllForUserAsync(userId)` signature example to include `PaginationQuery`.
- **`.claude/rules/transactions.md`** — update the **Per-kind access** section to reflect `PagedResult<TransactionBase>` and cross-link to `pagination.md`.
- **`.claude/rules/ef-pitfalls.md`** — the **Tracking** section's `.ToListAsync()` reference becomes `.ToPagedResultAsync(query)`; add a one-line note that `LongCountAsync()` does not interact with tracking.
- **`.claude/rules/transactions-shares.md`, `.claude/rules/counterparty.md`** — no changes; they describe write/share/resolve behavior, not list shapes.

---

## Verification

1. **Build:** `dotnet build` from `Fiolyn/`. Expect zero analyzer warnings — signature changes ripple through call sites we own.
2. **Unit tests:** `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj`. Cross-cutting change → run the full suite, not the per-file filter.
3. **Manual smoke:**
   - `GET /api/transactions?page=1&pageSize=5` → body `{items: [...], page: 1, pageSize: 5, totalCount: <n>, totalPages: <ceil(n/5)>}`.
   - `GET /api/transactions?page=2&pageSize=5` → second slice, no overlap with page 1.
   - `GET /api/transactions?pageSize=999` → 400 with validation error on `PageSize`.
   - `GET /api/spend-accounts` (no params) → defaults applied (page=1, pageSize=20).
   - `GET /api/admin/transactions` as admin → same shape.
   - `GET /api/stock-transactions?symbol=VNM&page=1&pageSize=10` → filter + pagination compose.
4. **Regression watch:** existing share/balance/concurrency tests must all still pass — pagination is read-only, so any red there indicates accidental coupling.
