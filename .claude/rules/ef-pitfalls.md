# EF Core Pitfalls

Read this when writing or debugging EF queries, includes, or write paths.

## Tracking

- **`.AsNoTracking()` on read-only list paths only.** Chain it before `.ToListAsync()` in `GetAllAsync` methods that project straight to DTOs. **Don't** add it to `GetByIdAsync` / `GetByIdForUserAsync` — callers fetch-then-mutate-then-update, and `SoftDeleteAsync` reuses the same fetch.
- A detached `Include`-bearing read forces `MarkIsDeletedModified` to call `Attach`, which walks the nav graph and tries to re-track the included entity — but a `FindAsync` from the write path has already tracked a *different* instance with the same key. EF throws `InvalidOperationException: another instance with the same key value is already being tracked`.
- Same trap fires when a shared read helper (e.g. `BuildReadQuery` in `StandardTransactionServiceBase` / `TransferTransactionService`) bakes `.AsNoTracking()` into the query — every single-row consumer inherits it silently. Keep the helper tracking-on; chain `.AsNoTracking()` on the list call sites instead.

## Includes vs Select projection

`Select(b => b.Members.OrderBy(...).ToList())` materializes `BrokerAccountMember` rows but leaves `m.AppUser` null — projections only pull what you name. If the consumer reads a downstream nav (e.g. `m.AppUser.Email`), use `.Include(b => b.Members).ThenInclude(m => m.AppUser)` and sort in memory after materialize, or extend the projector to assign `AppUser = m.AppUser` explicitly.

## LINQ predicate pitfalls

- **Hoist `.ToLowerInvariant()` out of EF predicates.** Compute the normalized comparand into a local first:
  ```csharp
  var lowered = name.ToLowerInvariant();
  ... b.Name.ToLower() == lowered
  ```
- **`IQueryable` predicates can't use `is T x` pattern bindings** (CS8122). Use bare type-test plus explicit cast: `t is StockTransaction && ((StockTransaction)t).BrokerAccount...`. See `TransactionReadService.GetAllForUserAsync`.

## Saving children with same-transaction parents

When a child references a parent staged in the same transaction, **set the nav, not the scalar FK**. A freshly-`AddAsync`-ed parent has `Id == 0` until `SaveChangesAsync`, so `child.ParentId = parent.Id` persists `0` and hits FK violation `23503`. Use `child.Parent = parent` — EF threads the generated parent ID through. Children attached to the parent's collection nav before `AddAsync(parent)` follow this implicitly.

## Many-to-many join entities

Prefer data annotations over `OnModelCreating`:
- `[PrimaryKey(nameof(A), nameof(B))]` for composite keys.
- `[Index(nameof(B), nameof(A))]` for the reverse-lookup index — the PK's leftmost column leaves the other direction as a seq scan otherwise.

Join rows representing presence (not mutable state) skip `IAuditable`/`ISoftDelete`. A single `JoinedAt` timestamp is enough; leaving means hard-deleting. See `SpendAccountMember`, `BrokerAccountMember`.

## Soft-delete query filter

`AppDbContext.OnModelCreating` registers `HasQueryFilter(e => !e.IsDeleted)` for every `ISoftDelete` entity, plus transitive cases (`BrokerAccountMember`/`SpendAccountMember`/`RefreshToken` filter on `AppUser.IsDeleted`).

- `IRepository.Query()` returns filtered rows by default — no `QueryActive()` variant. Chain `.IgnoreQueryFilters()` if you genuinely need soft-deleted rows.
- `IRepository.GetByIdAsync` is a `FindAsync` wrapper and *bypasses* the filter. Use `GetByIdOrThrowAsync` when "must be active" is part of the contract.
- The filter applies through `.Include` and nav traversals (`b.Members.Any(m => ...)`), so don't write redundant `!m.AppUser.IsDeleted` predicates.

## SoftDelete / Restore implementation

`Repository.SoftDelete` / `Restore` mark only `IsDeleted` modified via `dbContext.Entry(entity).Property(IsDeleted).IsModified = true` — not `Set<T>().Update(entity)`. The UPDATE touches only `IsDeleted` (and `LastModified` via the `IAuditable` hook), no risk of clobbering a column another transaction wrote between this load and save.

## Race-safe uniqueness

Map Postgres `23505` (unique-violation) to `ConflictException`. Pre-checks narrow the common case; the unique index is the actual race-safe guarantee. Catch `DbUpdateException` whose inner is `Npgsql.PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }` and rethrow. Live: `UserService.UpdateTelegramIdAsync`, `AllowedEmailService.CreateAsync`, `CounterpartyService.{Create,Update}Async`.

## Refresh-token rotation

`RefreshToken.RevokedAt` carries `[ConcurrencyCheck]`, so EF generates `UPDATE … WHERE Id=@id AND RevokedAt IS NULL`. Two concurrent `/auth/refresh` calls on the same token race; loser's `SaveChanges` throws `DbUpdateConcurrencyException` and the same transaction rolls back the new `RefreshToken` it just staged. `AuthService.RefreshAsync` catches this and surfaces 401. Don't reach for `ExecuteUpdate`. Tests can simulate the loser by stubbing `_refreshTokens.SaveAsync().ThrowsAsync(new DbUpdateConcurrencyException())`.

## Migrations

- Never `EnsureCreated()` (bypasses migrations, corrupts history).
- Never edit or delete *applied* migrations — create a new down migration instead.
- To undo an *unapplied* migration, use `dotnet ef migrations remove`, not hand-deletion.
- Migrations do NOT apply on startup. Run `dotnet ef database update` from `Fiolyn/` locally; deploy must run the equivalent before app boot.
- DbSet names are singular (`StockTransaction`, not `StockTransactions`).
