# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 10 web API with two responsibilities:

- **Telegram ingest** — receives webhook messages, resolves the sender's Telegram user ID to an `AppUser` via `AppUser.TelegramId` (no match → skip), saves the raw message as a page in a Notion database, and parses `stock buy/sell/div` commands into stock transaction records scoped to that `AppUser`.
- **Web REST API** — exposes CRUD endpoints for the personal-finance web app (stock transactions, broker accounts, and user-owned spend accounts), authenticated via Google SSO + Fiolyn-signed JWT.

**User model:** `AppUser` rows are created on first Google login, gated by an admin-managed `AllowedEmail` whitelist. Admins (`AppUser.IsAdmin = true`) can manage the whitelist and other users' roles.

**Repo layout:** `Fiolyn/` (app) · `Fiolyn.Tests/` (tests) · `docs/` (setup, plans).

**Stack:** C# 14 on .NET 10 · Northflank (Docker) · GitHub Actions → GHCR · Neon PostgreSQL · EF Core 10 · Notion API · Telegram Bot API · Google Sign-In · xUnit 2 · NSubstitute 5 · FluentAssertions 6

---

## Commands

From `Fiolyn/`:
```bash
dotnet run                                                    # run locally
dotnet ef migrations add <Name> --output-dir Data/Migrations  # new migration
dotnet ef database update                                     # apply migrations
```

From repo root:
```bash
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj                  # all tests
```

Build/publish is handled by the `Fiolyn/Dockerfile` — no manual `dotnet publish` needed.

---

## Configuration

`Fiolyn/appsettings.json` is tracked and intentionally minimal (only `Logging` + `AllowedHosts`). Secrets live in `Fiolyn/appsettings.Development.json` (gitignored) locally, or Northflank env vars (`__` for nested keys) in prod. Each section binds to a strongly-typed class in `ServiceRegisters/Settings/` via `IOptions<T>`.

See **`docs/ConfigurationSettingSetup.md`** for the full key list (JSON shape, env var names, required vs defaulted).

---

## Architecture

**Layered:** Controller (thin) → Service (business logic) → `IRepository<T>` (generic CRUD + composable queries) → `AppDbContext` → Neon Postgres.

- **Services** live in `Services/External/{Telegram,Notion,Google}/` for external integrations and `Services/Internal/` (implementations) + `Services/Internal/Interfaces/` (interfaces). Controllers inject interfaces. Services throw on errors — `Infrastructure/GlobalExceptionHandler` maps `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException` (project type in `Infrastructure/Exceptions/`) → 403, everything else → 500, and never leaks exception messages.
- **Repository** is generic (`IRepository<T>` in `Data/Repositories/`). Don't build entity-specific repositories.
- **Entities** implement `IAuditable` (Created/LastModified autopopulated on `SaveChangesAsync`) and usually `ISoftDelete` (`IsDeleted` flag). `RefreshToken` is the exception — revocation is explicit via `RevokedAt`, not soft-delete.
- **DI** is auto-discovered via `IServiceRegister` implementations in `ServiceRegisters/`, ordered by `Order`. Add new business services to `ScopedBusinessServicesRegister`. Use typed `AddHttpClient<TInterface, TImpl>` for HTTP clients (see `NotionServiceRegister`) — **don't** wrap with `AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>())`, that pins one `HttpClient` forever and defeats `IHttpClientFactory` handler rotation.
- **Migrations do NOT apply on startup.** Run `dotnet ef database update` from `Fiolyn/` locally, and ensure the equivalent runs as part of deploy (Northflank release step or a pre-start command) before the app boots — the app will fail on any query against an un-migrated schema. DbSet names are singular and match the entity class (`StockTransaction`, not `StockTransactions`).

**Auth:** see **`.claude/rules/auth.md`** for the flow and rules.

---

## Code Style

- **Formatting** is enforced by `.editorconfig` — 2-space indent (no tabs), Allman braces, max 120 chars/line, spaces inside parens (`public class Foo( ILogger logger )`, `if ( x > 0 )`, `method( arg )`), LF line endings, UTF-8, trailing newline, trimmed trailing whitespace.
- **Naming (enforced as `:error`):** PascalCase for public members, constants, and types; `_camelCase` for private fields (e.g. `_signingKeyBytes`); `I` prefix for interfaces (`IRepository`, `IJwtService`); `Async` suffix for `async`-keyword methods (`GetByIdAsync`).
- **File-scoped namespaces** required (`namespace Fiolyn.Services ;` — not block-scoped). Enforced as `:error`.
- **Braces required** on all `if` / `else` / `for` / `while` bodies — no single-line bodies. Enforced as `:warning`.
- **Primary constructors** (C# 12+) required for DI. `public class Foo( ILogger logger ) { }`.
- **`field` keyword** (C# 14) for auto-properties that need setter logic — don't hand-roll a backing field. Pattern: `public string Email { get ; set => field = NormalizeEmail( value ) ; } = string.Empty ;`. See `AppUser.Email`, `AllowedEmail.Email`, `StockTransaction.Symbol`.
- **Extension members** (C# 14) — use `extension( ClaimsPrincipal principal ) { ... }` blocks, not a static class of `this`-parameter methods. See `ClaimsPrincipalExtensions`.
- **`record` for DTOs** — one class per file, file name matches class name.
- **`var` when the type is apparent** (`:suggestion`). `async`/`await` for all I/O — never `.Result`/`.Wait()`. Sync methods for non-I/O repo ops (`Attach`, `Update`, `Delete`).
- **XML docs:** only on public endpoint methods. On interface implementations use `/// <inheritdoc />` — don't copy the interface `<summary>`.
- **JSON:** camelCase response keys (global `CamelCasePropertyNamesContractResolver` in `WebApiServiceRegister`).

---

## Key Rules

### HTTP / controllers

- **`TelegramController` always returns 200.** Telegram retries non-2xx; every other controller uses proper status codes (201 Created, 200 OK, 204 NoContent, 404 NotFound).
- **Never return raw entities** from REST controllers. Use `record` DTOs in `Controllers/DTOs/Responses/{Resource}/`. Use request records in `Controllers/DTOs/Requests/{Resource}/` with `[Required]` / `[MaxLength]` annotations.
- **Refetch through the service's read method when a Create response reads through a nav.** `AddAsync` + `SaveAsync` populates only the FK; the nav reference is null. The generic `IRepository<T>.GetByIdAsync` does *not* eager-load — service-layer read methods do (e.g. `StockTransactionService.GetByIdAsync` calls `.Include( t => t.BrokerAccount )`). Go back through the service before building the response DTO. See `StockTransactionController.CreateAsync`.
- **User-owned resources: enforce ownership in the service, not the controller.** When a resource belongs to a specific `AppUser` (has an `AppUserId` FK), give the service user-scoped methods (`GetAllForUserAsync(userId)`, `GetByIdForUserAsync(id, userId)`, `SoftDeleteAsync(id, userId)`) that filter by `AppUserId` and treat "not owned by caller" as 404. Controllers pipe `User.GetUserId()` into every call.
- **Shared / multi-member resources: membership = access.** When a resource can be shared across users (no direct `AppUserId` FK, joins via an `Xxx`/`XxxMember` table), user-scoped reads filter by membership: `.Where(r => r.Members.Any(m => m.AppUserId == userId && !m.AppUser.IsDeleted))`. "Not a member" is still 404, same as the single-owner case. Management endpoints for the member list (add by email, remove, list) live on the user controller and require the caller to be a member. See `SpendAccount` / `SpendAccountMember` / `SpendAccountService.EnsureCallerIsMemberAsync` and `BrokerAccount` / `BrokerAccountMember` / `BrokerAccountService.EnsureCallerIsMemberAsync`.
- **Transitive access for child resources.** When a child resource (e.g. `StockTransaction`) is owned by a shared parent (e.g. `BrokerAccount`), the child has no direct `AppUserId` or `Members` — access flows through the parent's membership. User-scoped reads on the child filter `.Where(c => c.Parent.Members.Any(m => m.AppUserId == userId && !m.AppUser.IsDeleted))`. For writes, prefer to inline the same membership filter into a single combined query that *also* projects whatever else the write needs (FK columns, related-entity scalars), rather than issuing a separate `EnsureUserIsParentMemberAsync` roundtrip — null result → 404, regardless of whether it was the row or the membership that's missing. Membership-resolving subqueries belong on the *child's* service, not the parent's — avoids a DI cycle. See `StockTransactionService.{Create,Update}Async` for the combined-query pattern.
- **Admin cross-user endpoints go in a separate `Admin<X>Controller`.** Route `/api/admin/<resource>`, `[Authorize(Policy = AuthConstants.AdminPolicyName)]` at class level, and the *same* service interface — add admin methods (no user scoping, usually with eager-loading for owner/member enrichment, e.g. `.Include(a => a.Members).ThenInclude(m => m.AppUser)`). See `AdminSpendAccountController` / `AdminBrokerAccountController` vs `SpendAccountController` / `BrokerAccountController`. The split avoids both response-shape overloading and policy-per-action sprawl.
- **Trusted-internal ingest bypass: name the method, don't nullable the arg.** When a background path (e.g. cron) genuinely can't identify a caller, expose a dedicated service method like `CreateFromCronAsync(entity)` rather than making `userId` nullable on the user-scoped `CreateAsync`. Document it as "for trusted internal callers only" in the interface XML docs. The Telegram ingest no longer qualifies — `TelegramMessageService` now resolves the sender to an `AppUser` via `AppUser.TelegramId` and calls the user-scoped `CreateAsync(tx, userId)`; there is currently no live example of this bypass in the repo.
- **Telegram ingest is gated by `AppUser.TelegramId`.** Linking is self-service (`PUT/DELETE /api/users/me/telegram-id`) or admin (`PUT/DELETE /api/users/{id}/telegram-id`); the column has a unique index and duplicate attempts surface as 409 via `ConflictException`. Stock writes over Telegram still go through `IStockTransactionService.CreateAsync` and so inherit the existing broker-membership check.
- **Every `BrokerAccount` references a `SpendAccount` (NOT NULL FK).** Stock transactions auto-adjust the linked balance: Buy → `−TotalAmount`, Sell/Dividend → `+TotalAmount`. Update reverses the old delta and applies the new (handles broker-change too); soft-delete reverses. The math lives in `StockTransactionService.ComputeBalanceDelta`. Balance writes go through `ISpendAccountBalanceWriter.ApplyDeltaAsync` which issues an atomic SQL `Balance = Balance + @delta` via `ExecuteUpdate` — never read-modify-write — so two concurrent stock-transaction operations on the same spend account can't lose an update. Atomicity across the balance write and the transaction insert/update is enforced by wrapping both in `IUnitOfWork.ExecuteInTransactionAsync` (the writer's `ExecuteUpdate` runs outside `SaveChanges`, so the wrapping transaction is required, not optional). Setting/changing `BrokerAccount.SpendAccountId` requires the caller to be a member of *both* accounts (`BrokerAccountService.EnsureUserIsSpendAccountMemberAsync`) — admin overload `UpdateAsync(account)` skips the per-user check but still verifies the spend account exists. Soft-deleting a `SpendAccount` is blocked while any active broker references it (`SpendAccountService.EnsureNotReferencedByActiveBrokerAsync` → `BadRequestException`).

### Auth

- **`Jwt:SigningKey` is mandatory, ≥32 chars.** App fails fast at startup (both `AuthServiceRegister` and `JwtService` ctor throw `InvalidOperationException`).
- **Admin bootstrap is manual** — insert `AllowedEmail`, log in once, `UPDATE AppUser.IsAdmin = true`. No code path auto-promotes.
- See **`.claude/rules/auth.md`** for the full auth rules (token rotation, claim constants, email normalization, `User.GetUserId()`, `AppUser` vs `User` collision).

### EF / repositories

- **`IRepository<T>` surface:** `Query()` / `QueryActive()` (composable `IQueryable`), `GetByIdAsync` / `GetActiveByIdAsync` / `GetActiveByIdOrThrowAsync`, `AddAsync` / `InsertRangeAsync`, `Attach` / `Update` / `UpdateRange`, `Delete` (hard) / `SoftDelete` / `Restore`, `SaveAsync`. Compose via `QueryActive()` when you need filters/projections; use the `GetById*` variants for single-entity fetches.
- **`IUnitOfWork.ExecuteInTransactionAsync(Func<Task>)` for explicit transactions** — use when a single logical operation mixes `ExecuteUpdate*` (which runs SQL outside `SaveChanges`) with normal tracked-entity writes; both must land or neither. Don't put transaction primitives on `IRepository<T>` — transactions are `DbContext`-scoped, not entity-scoped. Rollback is handled by `await using` disposal: if the action throws, the transaction is disposed without `CommitAsync` and Npgsql rolls back. Live example: `StockTransactionService.{Create,Update,SoftDelete}Async` wrap `ISpendAccountBalanceWriter.ApplyDeltaAsync` + the transaction's `SaveAsync`.
- **Use `GetActiveByIdOrThrowAsync` when "not found" is an error** — it throws `KeyNotFoundException` (→ 404 via `GlobalExceptionHandler`). Use `GetActiveByIdAsync` only when the caller needs to branch on null. Use `GetByIdAsync` only when you need soft-deleted rows (rare). None of these eager-load — add `.Include(...)` in the service layer when you need navs.
- **`.AsNoTracking()` on read-only list paths only** — chain it before `.ToListAsync()` in `GetAllAsync` methods that project straight to DTOs. **Don't** add it to `GetByIdAsync` paths; callers fetch-then-mutate-then-update.
- **Hoist `.ToLowerInvariant()` out of EF LINQ predicates.** Compute the normalized comparand into a local first, so only the column-side call ends up in the translated expression tree: `var lowered = name.ToLowerInvariant(); ... b.Name.ToLower() == lowered`. See `StockTransactionService.ResolveBrokerAsync`.
- **When a child references a parent staged in the same transaction, set the nav, not the scalar FK.** A freshly-`AddAsync`-ed parent has `Id == 0` until `SaveChangesAsync` round-trips to the DB, so `child.ParentId = parent.Id` persists `0` and hits a FK violation (`23503`). Use `child.Parent = parent` — EF fills in the FK after the parent insert within the same transaction. See `AuthService.IssueTokensAsync` assigning `RefreshToken.User = user` on first-time Google login. Children attached to the parent's collection nav (`parent.Members.Add(child)` before `AddAsync(parent)`) follow the same rule implicitly — EF threads the generated parent ID through to the join-table insert. See `SpendAccountService.CreateAsync`.
- **For explicit many-to-many join entities, prefer data annotations over `OnModelCreating`.** Use `[PrimaryKey(nameof(A), nameof(B))]` for composite keys and `[Index(nameof(B), nameof(A))]` for the reverse-lookup index (the PK's leftmost column leaves the other direction as a seq scan). Join rows that represent presence, not mutable state, should skip `IAuditable`/`ISoftDelete` — a single manually-set timestamp like `JoinedAt` is enough; leaving means hard-deleting the row. See `SpendAccountMember` and `BrokerAccountMember`.
- **`StockTransaction.Symbol` auto-normalizes** to uppercase via the setter. Don't manually `ToUpperInvariant()` on assignment.
- **Money and quantity fields are `long`, not `decimal`.** The primary currency is VND, which has no fractional subunit, and VN exchanges trade in whole-share lots. Applies to balances, prices, totals, quantities — entity, DTO, and `Parse` layer. See `SpendAccount.Balance`, `StockTransaction.{Quantity,Price,TotalAmount}`. Only reintroduce `decimal` if a genuine sub-unit requirement appears (confirm first).
- **Migrations:** never `EnsureCreated()` (bypasses migrations, corrupts history). Never edit or delete applied migrations — create a new down migration instead. To undo an *unapplied* migration, use `dotnet ef migrations remove`, not hand-deletion.

### Deployment (Northflank)

- **Do NOT use `UseHttpsRedirection`.** Northflank's reverse proxy sends plain HTTP; redirect causes request failures.
- App listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`). Northflank terminates TLS externally.
- Push to `master` → GitHub Actions builds `Fiolyn/Dockerfile` (context = repo root) → pushes to GHCR → Northflank auto-deploys. GH Actions secret: `NORTHFLANK_API_TOKEN`.

### Workflow

- **After every code change:** call `mcp__ide__getDiagnostics` if available (else run `dotnet build` from the repo root) to catch compile/analyzer errors, then run **only the test classes covering the files you touched** via `--filter`, e.g.
  `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~AuthControllerTests"`.
  Map each changed production file to its `*Tests.cs` (e.g. `AuthService.cs` → `AuthServiceTests`; a new `Controllers/FooController.cs` → `FooControllerTests`). Only fall back to the full suite (`dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj`) when the change is cross-cutting (e.g. `IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes) **or** as the pre-commit gate — all tests must pass before committing.
