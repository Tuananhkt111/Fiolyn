# Per-User `Counterparty` Entity — Replace `CounterpartyName` String

## Context

`LendTransaction` and `BorrowingTransaction` carry the other party as a free-text `string CounterpartyName` (max 100). That makes the data unmanageable: no reusable picker, no rename-once, typo / case duplicates, no place to hang future metadata (phone, notes), no referential integrity.

This refactor introduces a per-`AppUser` `Counterparty` entity (private contact list) and replaces the string column on the entity with a required `CounterpartyId` FK on both leaves. The unified read path shows the counterparty name to anyone who can already read the transaction (i.e. SpendAccount co-members). Only the owning user can rename / delete a `Counterparty` directly via `/api/counterparties`, but writes to lend/borrow stay name-driven: the request still carries `CounterpartyName` and the service does **find-or-create** against the caller's contact list — no client-side picker required.

**User-confirmed decisions:**
- Ownership = per-`AppUser` (not shared via SpendAccount membership).
- Schema = replace `CounterpartyName` with required `CounterpartyId` FK on the entity; drop the string column entirely.
- **Write API contract = `CounterpartyName` (string) on `LendTransactionRequest` / `BorrowingTransactionRequest`. The service resolves to a `Counterparty` row, creating one for the caller if no case-insensitive match exists.**
- Metadata = `Name` only for now; phone/email/notes/`AppUser` link deferred.

---

## Target shape

### Entity
`Fiolyn/Entities/Counterparty.cs` — `IAuditable`, `ISoftDelete`, primary key `Id`, required `AppUserId` (+ `AppUser` nav), `[Required][MaxLength(100)] Name` with `field`-keyword setter that `Trim()`s. No `[Index]` data annotation; the unique index is configured in `OnModelCreating` + raw-SQL expression index (see Migration).

### DbContext (`Fiolyn/Data/AppDbContext.cs`)
- `DbSet<Counterparty> Counterparty`.
- FK Counterparty → AppUser, `OnDelete(Restrict)`.
- FK LendTransaction → Counterparty, `OnDelete(Restrict)` (and same for Borrowing).
- Soft-delete query filter: `c => !c.IsDeleted` only — **do not** also filter on `!c.AppUser.IsDeleted`. If a soft-deleted owner's row got filtered out, eager-loaded `Counterparty` navs on transactions read by SpendAccount co-members would become null and crash the response builder.
- Property-based unique index `(AppUserId, Name)` is **not** added at the EF level; instead the migration creates a Postgres expression index `UNIQUE (AppUserId, LOWER(Name))` so case-only differences collide. Service layer maps `PostgresException { SqlState: "23505" }` → `ConflictException`.

### Lend & Borrowing entity changes
Drop `[Required] [MaxLength(100)] string CounterpartyName`. Add `[Required] long CounterpartyId; Counterparty Counterparty = null!;`. Keep `DueDate?` unchanged.

### Service: `ICounterpartyService` / `CounterpartyService`
- `GetAllForUserAsync(userId)` — list filtered by `AppUserId == userId`, `.AsNoTracking().OrderBy(Name)`.
- `GetByIdForUserAsync(id, userId)` — returns null if not owned (controller maps to 404).
- `CreateAsync(name, userId)` — trim + lowercase pre-check (hoist `lowered = name.Trim().ToLowerInvariant()` per CLAUDE.md predicate-hoist rule) → `ConflictException`; race-safe fallback catches inner `PostgresException.UniqueViolation`. Pattern mirrors `AllowedEmailService.CreateAsync`.
- `UpdateAsync(id, userId, name)` — load via `GetByIdForUserAsync`, mutate `Name` directly (do **not** call `repository.Update(...)` — rely on EF change tracker per CLAUDE.md), `SaveAsync`, same conflict mapping.
- `SoftDeleteAsync(id, userId)` — calls private `EnsureNotReferencedByActiveTransactionAsync(id)` which `AnyAsync` over `IRepository<LendTransaction>` and `IRepository<BorrowingTransaction>` filtered by `CounterpartyId == id`; throws `BadRequestException` on hit. Mirrors `SpendAccountService.EnsureNotReferencedByActiveTransactionAsync`.

Inject the service in `ServiceRegisters/ScopedBusinessServicesRegister.cs`.

### Controller: `CounterpartyController`
Route `api/counterparties`, class-level `[Authorize]`, primary-ctor injecting `ICounterpartyService`. Endpoints `GET /`, `GET /{id}` (named route for `CreatedAtRoute`), `HEAD /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`. Each pipes `User.GetUserId()` into the service. Mirrors the shape of `SpendAccountController` but without member endpoints.

### DTOs
- Requests: `CounterpartyRequest(string Name)` with `[Required][MaxLength(100)]`.
- Responses: `CounterpartyResponse(long Id, string Name, DateTime Created, DateTime LastModified)` plus a small `CounterpartyRefResponse(long Id, string Name)` mirroring `TransactionCategoryRefResponse` for embedding.

### Hooking into Lend/Borrowing services
The write-side contract is name-driven, so the leaves resolve `CounterpartyName` → `CounterpartyId` themselves rather than validating an FK supplied by the client.

Add a `CounterpartyResolver` helper (`Fiolyn/Services/Internal/Helpers/CounterpartyResolver.cs`) — single method `Task<Counterparty> ResolveOrCreateAsync(IRepository<Counterparty> repo, string rawName, long userId)`:
1. Trim, reject empty (`BadRequestException`).
2. `var lowered = trimmed.ToLowerInvariant();` (hoisted out of the predicate per CLAUDE.md).
3. `repo.Query().FirstOrDefaultAsync(c => c.AppUserId == userId && c.Name.ToLower() == lowered)` — return on hit.
4. Otherwise `AddAsync` a new `Counterparty { AppUserId = userId, Name = trimmed }` — staged but not saved. The leaf service's subsequent `SaveAsync` commits both rows in the same transaction, so a failed lend/borrow write rolls back the new Counterparty too (no orphan rows). Assign `entity.Counterparty = resolved`, not the scalar FK, so EF threads the parent ID through after `SaveChangesAsync` even for newly-staged Counterparties (see CLAUDE.md "When a child references a parent staged in the same transaction").

Add a marker interface `IHasCounterparty` (`Entities/IHasCounterparty.cs`) implemented by `LendTransaction` and `BorrowingTransaction` — exposes `long CounterpartyId` + `Counterparty Counterparty`.

Add a shared base `CounterpartyTransactionServiceBase<T>` (`Services/Internal/BaseServices/CounterpartyTransactionServiceBase.cs`) sitting between `StandardTransactionServiceBase<T>` and the leaf services, generic on `T : TransactionBase, IHasCounterparty`. It injects `IRepository<Counterparty>`, exposes `CreateAsync(T, long, string)` and `UpdateAsync(T, long, string)` overloads that call the resolver and assign `tx.Counterparty = resolved` before delegating to the standard base, and overrides `BuildReadQuery` to chain `.Include(nameof(IHasCounterparty.Counterparty))`. The leaf services (`LendTransactionService`, `BorrowingTransactionService`) collapse to a 4-line shell — just the `Kind` override. No `ValidateLeafAsync` hook on the standard base; the existing `TransactionCategoryGuard` flow runs unchanged inside the standard base after the resolver assigns the nav.

`UpdateAsync` short-circuits the resolver round-trip when `transaction.Counterparty.Name` already matches the inbound name (case-insensitive, trimmed) — PUTs that change non-counterparty fields skip the extra DB query.

### Lend / Borrowing DTOs and controllers
- `LendTransactionRequest` / `BorrowingTransactionRequest` — **keep `[Required] [MaxLength(100)] string CounterpartyName`** unchanged. Same wire contract as today.
- `LendTransactionResponse` / `BorrowingTransactionResponse` — replace `string CounterpartyName` with `CounterpartyRefResponse Counterparty` so the client sees both the resolved `Id` (useful if the UI later wants to manage the contact via `/api/counterparties`) and the `Name`.
- `LendTransactionController` / `BorrowingTransactionController` — Create and Update still pass `request.CounterpartyName` straight through to the service; no change to controller logic beyond switching the response shape. Keep the existing unconditional refetch on update (Lend/Borrowing already do this); the new `BuildReadQuery` override means the refetched entity carries the fresh `Counterparty` nav.

**Update semantics:** if the user changes `CounterpartyName` on an existing lend (e.g. "John" → "Jon"), the resolver creates a new `Counterparty` row "Jon" (or reuses an existing one) and the lend's `CounterpartyId` is repointed. **The original "John" row is not renamed** — that would silently mutate every other transaction pointing at it. To rename the contact globally, the user calls `PUT /api/counterparties/{id}`.

### Unified read path
`Fiolyn/Services/Internal/TransactionReadService.cs` — extend `BuildQuery()` with two more polymorphic includes using the existing `(t as Sub)!.Nav` pattern already present for `StockTransaction.BrokerAccount` and `TransferTransaction.DestinationSpendAccount`:
```
.Include( t => ( t as LendTransaction )!.Counterparty )
.Include( t => ( t as BorrowingTransaction )!.Counterparty )
```
`Fiolyn/Controllers/DTOs/Responses/Transaction/TransactionResponse.cs` — replace `TransactionCounterpartyDetail(string CounterpartyName, DateTime? DueDate)` with `TransactionCounterpartyDetail(long CounterpartyId, string CounterpartyName, DateTime? DueDate)`; in the `switch`, read `l.Counterparty.Id` / `l.Counterparty.Name` (nav non-null, FK is required).

---

## Migration

`dotnet ef migrations add AddCounterparty --project Fiolyn` from the repo root.

`Up`:
1. `CreateTable("Counterparty", ...)` with `Id bigserial PK`, `AppUserId bigint NOT NULL`, `Name varchar(100) NOT NULL`, `Created`, `LastModified`, `IsDeleted bool NOT NULL DEFAULT false`. FK to `AppUser(Id)` ON DELETE RESTRICT.
2. `CreateIndex` on `AppUserId`.
3. Raw SQL: `CREATE UNIQUE INDEX "IX_Counterparty_AppUserId_LowerName" ON "Counterparty" ("AppUserId", LOWER("Name"))`.
4. `AddColumn<long>("CounterpartyId", "LendTransaction", nullable: true)` and same for `BorrowingTransaction`.
5. **Backfill** (single SQL block, deduped across both leaves):
   - `INSERT INTO "Counterparty" (AppUserId, Name, Created, LastModified, IsDeleted)` selecting `DISTINCT ON (owner_id, LOWER(TRIM(name)))` rows from a `UNION ALL` of `LendTransaction ⨝ Transaction` and `BorrowingTransaction ⨝ Transaction`, where `owner_id = COALESCE(b."PayerId", first SpendAccountMember by JoinedAt for b."SpendAccountId")`. Filter out NULL/empty names.
   - Two `UPDATE` statements (one per leaf table) joining `(owner_id, LOWER(TRIM(name)))` against the freshly-inserted Counterparty rows to set `CounterpartyId`.
6. **Pre-flight assert** any surviving `CounterpartyId IS NULL` rows fail loudly (`RAISE EXCEPTION` in a `DO $$ … $$` block) — guards against the edge case of a transaction whose SpendAccount has zero members.
7. `AlterColumn` `CounterpartyId` to `nullable: false` on both leaves; `AddForeignKey` to `Counterparty(Id)` ON DELETE RESTRICT.
8. `DropColumn("CounterpartyName")` on both leaves.

`Down`:
1. `AddColumn<string>("CounterpartyName", maxLength: 100, nullable: true)` on both leaves.
2. `UPDATE "LendTransaction" SET "CounterpartyName" = c."Name" FROM "Counterparty" c WHERE c."Id" = "LendTransaction"."CounterpartyId";` and same for borrowing.
3. `AlterColumn` to `nullable: false` (matches original schema).
4. Drop FK + drop `CounterpartyId` on both.
5. Drop `IX_Counterparty_AppUserId_LowerName`, `DropTable("Counterparty")`.

---

## Tests
Add new `xUnit` classes under `Fiolyn.Tests/`:
- `Unit/Services/CounterpartyServiceTests.cs` — list/get scoping, create dedupe (case-insensitive), update conflict, soft-delete blocked while referenced (seed both lend & borrow), 23505 → `ConflictException`.
- `Unit/Controllers/CounterpartyControllerTests.cs` — happy paths + 404 + 400.
- `Unit/Services/LendTransactionServiceTests.cs` and `BorrowingTransactionServiceTests.cs` — new files; cover the `CounterpartyResolver` find-or-create path (existing match by case-insensitive name reused; new name inserted; empty/whitespace name → 400; resolved Counterparty scoped to caller `userId`), update-with-name-change creates a new Counterparty rather than renaming, happy create/update/delete, kind-guard interaction unaffected, transactional rollback (lend save fails → no orphan Counterparty).
- `Unit/Controllers/LendTransactionControllerTests.cs` and `BorrowingTransactionControllerTests.cs` — new files.

Re-run the unified-read tests (e.g. `TransactionReadServiceTests`, controller tests for `TransactionsController`) to confirm the new `TransactionCounterpartyDetail` shape and the polymorphic `Include` paths still produce expected DTOs.

---

## Docs
Update `Fiolyn/.claude/CLAUDE.md` HTTP/controllers section: add a paragraph after the `TransactionCategory` bullet describing the per-user `Counterparty` pattern — owned via `AppUserId`, referenced by `LendTransaction.CounterpartyId` / `BorrowingTransaction.CounterpartyId` (Required, Restrict on delete), gated by `CounterpartyGuard.EnsureOwnedByUserAsync` from `StandardTransactionServiceBase.ValidateLeafAsync`; co-members of the SpendAccount can read the counterparty *name* via the eager-loaded nav but cannot edit/delete the underlying Counterparty row; soft-delete blocks while referenced by any active lend/borrow.

This is required by the saved feedback memory (`feedback_update_claude_md.md`) — refactors that change project-wide patterns must update CLAUDE.md alongside the code.

---

## Critical files

**To create:**
- `Fiolyn/Entities/Counterparty.cs`
- `Fiolyn/Entities/IHasCounterparty.cs`
- `Fiolyn/Services/Internal/Interfaces/ICounterpartyService.cs`
- `Fiolyn/Services/Internal/CounterpartyService.cs`
- `Fiolyn/Services/Internal/Helpers/CounterpartyResolver.cs`
- `Fiolyn/Services/Internal/BaseServices/CounterpartyTransactionServiceBase.cs`
- `Fiolyn/Controllers/CounterpartyController.cs`
- `Fiolyn/Controllers/DTOs/Requests/Counterparty/CounterpartyRequest.cs`
- `Fiolyn/Controllers/DTOs/Responses/Counterparty/CounterpartyResponse.cs` + `CounterpartyRefResponse.cs`
- `Fiolyn/Data/Migrations/<timestamp>_AddCounterparty.cs` (+ designer + snapshot regen)
- New test files: `CounterpartyServiceTests`, `CounterpartyControllerTests`, `LendTransactionServiceTests`, `BorrowingTransactionServiceTests`.

**To modify:**
- `Fiolyn/Entities/LendTransaction.cs` · `BorrowingTransaction.cs`
- `Fiolyn/Data/AppDbContext.cs`
- `Fiolyn/Services/Internal/LendTransactionService.cs` · `BorrowingTransactionService.cs`
- `Fiolyn/Services/Internal/TransactionReadService.cs`
- `Fiolyn/Controllers/DTOs/Responses/LendTransaction/LendTransactionResponse.cs` · `BorrowingTransaction/BorrowingTransactionResponse.cs` (request DTOs unchanged)
- `Fiolyn/Controllers/DTOs/Responses/Transaction/TransactionResponse.cs`
- `Fiolyn/Controllers/LendTransactionController.cs` · `BorrowingTransactionController.cs`
- `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs`
- `Fiolyn/.claude/CLAUDE.md`

---

## Tricky bits to watch at implementation time

1. **Backfill dedup across both leaves.** Run a single deduped `INSERT` from a `UNION ALL` of lend + borrow before either `UPDATE`, otherwise `(userA, "John")` lends and borrows insert two distinct Counterparty rows.
2. **Owner inference for `PayerId IS NULL` rows.** Fall back to first SpendAccountMember by `JoinedAt`. The `RAISE EXCEPTION` pre-flight guards against the (theoretical) zero-member edge case before `NOT NULL` is enforced.
3. **Polymorphic Include syntax.** Use the existing `(t as LendTransaction)!.Counterparty` pattern that `TransactionReadService.BuildQuery` already uses for `StockTransaction.BrokerAccount` — verified on read.
4. **Don't double-filter soft-delete.** Counterparty's query filter is `!c.IsDeleted` only. Adding `!c.AppUser.IsDeleted` would null out eager-loaded navs on transactions read by co-members and crash the DTO mapper.
5. **Expression unique index won't appear in the model snapshot.** That's expected — keep it as raw SQL. Don't also add a property-based `HasIndex(..).IsUnique()` or you'll get redundant 23505s on case-difference inserts the expression index would have caught.
6. **Conditional refetch on FK change.** The existing Lend/Borrowing controllers refetch unconditionally on update — keep that for consistency. CLAUDE.md's "FK actually changed" optimization is followed by BrokerAccount but not by lend/borrow today; not in scope here.
7. **Trim normalization in setter + service.** The `field` setter trims, so service-layer pre-check comparisons must compare trimmed values. Hoist `var lowered = name.Trim().ToLowerInvariant();` out of the EF predicate per CLAUDE.md.

---

## Verification

After implementation:
1. `mcp__ide__getDiagnostics` (or `dotnet build` from repo root) — clean.
2. `dotnet ef database update --project Fiolyn` against a copy of dev data — confirm backfill yields one Counterparty per `(owner, distinct LOWER(TRIM(name)))` and every lend/borrow row gets a non-null FK.
3. Targeted tests:
   `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~Counterparty|FullyQualifiedName~LendTransaction|FullyQualifiedName~BorrowingTransaction|FullyQualifiedName~TransactionRead|FullyQualifiedName~StandardTransactionServiceBase"`.
4. Pre-commit gate: full suite `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` (cross-cuts `StandardTransactionServiceBase` and `AppDbContext`).
5. Manual spot-check via HTTP:
   - `POST /api/counterparties` creates; `POST` again with same name (different case) → 409.
   - `POST /api/lend-transactions` with `CounterpartyName: "John"` (new) → creates the Counterparty + lend in one transaction; response carries `Counterparty.Id`.
   - Repeat with `CounterpartyName: "JOHN"` → reuses the same `Counterparty.Id` (case-insensitive).
   - `PUT /api/lend-transactions/{id}` with a different `CounterpartyName` → repoints to a new/existing Counterparty; the original Counterparty row is untouched.
   - `GET /api/transactions` as a SpendAccount co-member shows the counterparty `name` on lend/borrow rows.
   - `DELETE /api/counterparties/{id}` while referenced → 400; soft-delete the lend/borrow first → 204.
