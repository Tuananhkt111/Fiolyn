# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project Overview

Fiolyn is a .NET 10 web API with two responsibilities:

- **Telegram ingest** — receives webhook messages, resolves the sender's Telegram user ID to an `AppUser` via `AppUser.TelegramId` (no match → skip), saves the raw message as a Notion page, and parses `stock buy/sell/div` commands into stock transaction records scoped to that `AppUser`.
- **Web REST API** — CRUD for the personal-finance web app: spend accounts, broker accounts, and seven transaction kinds (Income, Expense, Lend, Borrowing, Transfer, Adjustment, Stock) — authenticated via Google SSO + Fiolyn-signed JWT.

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

Build/publish is handled by `Fiolyn/Dockerfile`.

---

## Configuration

`Fiolyn/appsettings.json` is tracked and intentionally minimal (`Logging` + `AllowedHosts`). Secrets live in `Fiolyn/appsettings.Development.json` (gitignored) locally, or Northflank env vars (`__` for nested keys) in prod. Each section binds to a strongly-typed class in `ServiceRegisters/Settings/` via `IOptions<T>`.

Full key list: **`docs/ConfigurationSettingSetup.md`**.

---

## Architecture

**Layered:** Controller (thin) → Service (business logic) → `IRepository<T>` (generic CRUD + composable queries) → `AppDbContext` → Neon Postgres.

- **Services** live in `Services/External/{Telegram,Notion,Google}/` (external integrations) and `Services/Internal/` with subfolders `Interfaces/`, `BaseServices/` (abstract bases + service DTOs), `Helpers/` (stateless utilities). Controllers inject interfaces. Services throw on errors — `Infrastructure/GlobalExceptionHandler` maps `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException` → 403, `BadRequestException` → 400, `ConflictException` → 409, else 500. Never leaks exception messages.
- **Repository** is generic (`IRepository<T>` in `Data/Repositories/`). Don't build entity-specific repositories.
- **Entities** implement `IAuditable` (Created/LastModified autopopulated on `SaveChangesAsync`) and usually `ISoftDelete`. `RefreshToken` is the exception — explicit revocation via `RevokedAt`.
- **DI** auto-discovered via `IServiceRegister` implementations in `ServiceRegisters/`, ordered by `Order`. Add new business services to `ScopedBusinessServicesRegister`. Use typed `AddHttpClient<TInterface, TImpl>` — **don't** wrap with `AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>())`, which pins one `HttpClient` forever and defeats `IHttpClientFactory` handler rotation.

- **Auth:** see `.claude/rules/auth.md`.
- **Access control / ownership / admin endpoints:** see `.claude/rules/access-control.md`.
- **Transactions (TransactionBase, leaves, balance writes, categories):** see `.claude/rules/transactions.md`.
- **Income/Expense shares (auto-paired Lend/Borrow children):** see `.claude/rules/transactions-shares.md`.
- **Counterparty / Lend / Borrow / Obligations:** see `.claude/rules/counterparty.md`.
- **EF pitfalls (tracking, includes, projections, migrations):** see `.claude/rules/ef-pitfalls.md`.

---

## Code Style

Formatting and naming are enforced by `.editorconfig` (2-space indent, Allman braces, 120-col, spaces inside parens, file-scoped namespaces, braces required, PascalCase / `_camelCase` / `I`-prefix / `Async`-suffix). Read it for specifics.

- **Primary constructors** (C# 12+) for DI: `public class Foo( ILogger logger ) { }`.
- **`field` keyword** (C# 14) for auto-properties needing setter logic — don't hand-roll a backing field. See `AppUser.Email`, `AllowedEmail.Email`, `StockTransaction.Symbol`.
- **Extension members** (C# 14) — `extension( ClaimsPrincipal principal ) { ... }` blocks, not static `this`-parameter methods. See `ClaimsPrincipalExtensions`.
- **`record` for DTOs** — one class per file, file name matches class.
- **`var` when type is apparent.** `async`/`await` for all I/O — never `.Result`/`.Wait()`. Sync methods for non-I/O repo ops (`Attach`, `Update`, `Delete`).
- **XML docs:** only on public endpoint methods. Interface implementations use `/// <inheritdoc />`.
- **JSON:** camelCase response keys (global `CamelCasePropertyNamesContractResolver`).
- **Money / quantity = `long`, not `decimal`.** Primary currency is VND (no fractional subunit); VN exchanges trade whole-share lots. Applies to balances, prices, totals, quantities — entity, DTO, `Parse` layer. Only reintroduce `decimal` if a genuine sub-unit requirement appears (confirm first).

---

## Key Rules

### HTTP / controllers

- **`TelegramController` always returns 200** (Telegram retries non-2xx). Other controllers use real status codes.
- **Never return raw entities** from REST controllers. Use `record` DTOs in `Controllers/DTOs/Responses/{Resource}/`. Request records in `Controllers/DTOs/Requests/{Resource}/` with `[Required]` / `[MaxLength]`.
- **Refetch through the service's read method when a write response reads through a nav.** Two cases:
  - *Create:* `AddAsync` + `SaveAsync` populates only the FK; the nav is null.
  - *Update where an FK actually changed:* the in-memory nav is stale (EF doesn't refresh it when you reassign the scalar FK). Capture old FK, compare, refetch only when changed.

  `IRepository<T>.GetByIdAsync` does *not* eager-load — service-layer reads do (`.Include(...)`). See `BrokerAccountController.UpdateAsync`, `StockTransactionController.UpdateAsync`.

### Deployment (Northflank)

- **Do NOT use `UseHttpsRedirection`.** Northflank's reverse proxy sends plain HTTP.
- App listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`); Northflank terminates TLS externally.
- Push to `master` → GitHub Actions builds `Fiolyn/Dockerfile` (context = repo root) → GHCR → Northflank auto-deploys. GH Actions secret: `NORTHFLANK_API_TOKEN`.

### Workflow

- **After every code change:** call `mcp__ide__getDiagnostics` if available (else `dotnet build`) to catch compile/analyzer errors, then run **only the test classes covering the files you touched** via `--filter`:
  ```
  dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~AuthControllerTests"
  ```
  Map each changed production file to its `*Tests.cs`. Fall back to the full suite only when the change is cross-cutting (`IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes) **or** as the pre-commit gate — all tests must pass before committing.
