# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 10 web API with two responsibilities:

- **Telegram ingest** — receives webhook messages, checks the sender against a DB whitelist (`TelegramUserInfo`), saves the raw message as a page in a Notion database, and parses `stock buy/sell/div` commands into stock transaction records.
- **Web REST API** — exposes CRUD endpoints for the stock-management web app, authenticated via Google SSO + Fiolyn-signed JWT.

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
- **Migrations** apply on startup via `app.InitializeDatabaseAsync()` (`Data/Seeds/DatabaseInitializer`). DbSet names are singular and match the entity class (`StockTransaction`, not `StockTransactions`).

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

### Auth

- **`Jwt:SigningKey` is mandatory, ≥32 chars.** App fails fast at startup (both `AuthServiceRegister` and `JwtService` ctor throw `InvalidOperationException`).
- **Admin bootstrap is manual** — insert `AllowedEmail`, log in once, `UPDATE AppUser.IsAdmin = true`. No code path auto-promotes.
- See **`.claude/rules/auth.md`** for the full auth rules (token rotation, claim constants, email normalization, `User.GetUserId()`, `AppUser` vs `User` collision).

### EF / repositories

- **`IRepository<T>` surface:** `Query()` / `QueryActive()` (composable `IQueryable`), `GetByIdAsync` / `GetActiveByIdAsync` / `GetActiveByIdOrThrowAsync`, `AddAsync` / `InsertRangeAsync`, `Attach` / `Update` / `UpdateRange`, `Delete` (hard) / `SoftDelete` / `Restore`, `SaveAsync`. Compose via `QueryActive()` when you need filters/projections; use the `GetById*` variants for single-entity fetches.
- **Use `GetActiveByIdOrThrowAsync` when "not found" is an error** — it throws `KeyNotFoundException` (→ 404 via `GlobalExceptionHandler`). Use `GetActiveByIdAsync` only when the caller needs to branch on null. Use `GetByIdAsync` only when you need soft-deleted rows (rare). None of these eager-load — add `.Include(...)` in the service layer when you need navs.
- **`.AsNoTracking()` on read-only list paths only** — chain it before `.ToListAsync()` in `GetAllAsync` methods that project straight to DTOs. **Don't** add it to `GetByIdAsync` paths; callers fetch-then-mutate-then-update.
- **Hoist `.ToLowerInvariant()` out of EF LINQ predicates.** Compute the normalized comparand into a local first, so only the column-side call ends up in the translated expression tree: `var lowered = name.ToLowerInvariant(); ... b.Name.ToLower() == lowered`. See `StockTransactionService.ResolveBrokerAsync`.
- **When a child references a parent staged in the same transaction, set the nav, not the scalar FK.** A freshly-`AddAsync`-ed parent has `Id == 0` until `SaveChangesAsync` round-trips to the DB, so `child.ParentId = parent.Id` persists `0` and hits a FK violation (`23503`). Use `child.Parent = parent` — EF fills in the FK after the parent insert within the same transaction. See `AuthService.IssueTokensAsync` assigning `RefreshToken.User = user` on first-time Google login.
- **`StockTransaction.Symbol` auto-normalizes** to uppercase via the setter. Don't manually `ToUpperInvariant()` on assignment.
- **Migrations:** never `EnsureCreated()` (bypasses migrations, corrupts history). Never edit or delete applied migrations — create a new down migration instead. To undo an *unapplied* migration, use `dotnet ef migrations remove`, not hand-deletion.

### Deployment (Northflank)

- **Do NOT use `UseHttpsRedirection`.** Northflank's reverse proxy sends plain HTTP; redirect causes request failures.
- App listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`). Northflank terminates TLS externally.
- Push to `master` → GitHub Actions builds `Fiolyn/Dockerfile` (context = repo root) → pushes to GHCR → Northflank auto-deploys. GH Actions secret: `NORTHFLANK_API_TOKEN`.

### Workflow

- **After every code change:** call `mcp__ide__getDiagnostics` if available (else run `dotnet build` from the repo root) to catch compile/analyzer errors, then run `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` (all should pass) before committing.
