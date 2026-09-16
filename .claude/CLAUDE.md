# CLAUDE.md

Guidance for Claude Code working in this repository. This file is the **index** — it routes to the
rules, it doesn't restate them.

## Project Overview

Fiolyn is a .NET 10 web API with two front doors, both writing to one ledger:

- **Telegram ingest** — webhook resolves the sender to an `AppUser` via `AppUser.TelegramId` (no match → skip), records `stock buy/sell/div` commands as transactions, files everything else to Notion.
- **Web REST API** — spend accounts, broker accounts and seven transaction kinds (Income, Expense, Lend, Borrowing, Transfer, Adjustment, Stock), behind Google SSO + Fiolyn-signed JWT.

`AppUser` rows are created on first Google login, gated by an admin-managed `AllowedEmail` whitelist.

**Stack:** C# 14 on .NET 10 · EF Core 10 · Neon PostgreSQL · Northflank (Docker) · GitHub Actions → GHCR · Notion · Telegram Bot API · Google Sign-In · xUnit · NSubstitute · FluentAssertions

**Layout:** `Fiolyn/` (app) · `Fiolyn.Tests/` (tests) · `docs/` (see [Documentation](#documentation)) · `planning/` (transient working material, not documentation)

---

## Commands

```bash
cd Fiolyn && dotnet run                                       # run locally (:5027)
cd Fiolyn && dotnet ef database update                        # apply migrations
cd Fiolyn && dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj                  # from repo root
```

Details: `docs/guides/local-development.md`. Build/publish is handled by `Fiolyn/Dockerfile`.

## Configuration

Secrets in `Fiolyn/appsettings.Development.json` (gitignored) locally, Northflank env vars (`__` for nested keys) in prod. Each section binds to a class in `ServiceRegisters/Settings/` via `IOptions<T>`.

Full key list: **`docs/guides/configuration.md`**.

---

## Architecture

**Layered:** Controller (thin) → Service (business logic) → `IRepository<T>` (generic CRUD + composable queries) → `AppDbContext` → Neon Postgres.

- **Services** live in `Services/External/{Telegram,Notion,Google}/` and `Services/Internal/` with subfolders `Interfaces/`, `BaseServices/`, `Helpers/`. Controllers inject interfaces. Services throw; `Infrastructure/GlobalExceptionHandler` maps `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException` → 403, `BadRequestException` → 400, `ConflictException` → 409, else 500. Never leaks exception messages.
- **Repository** is generic (`IRepository<T>` in `Data/Repositories/`). Don't build entity-specific repositories.
- **Entities** implement `IAuditable` and usually `ISoftDelete`. `RefreshToken` is the exception — explicit revocation via `RevokedAt`.
- **DI** auto-discovered via `IServiceRegister` implementations in `ServiceRegisters/`, ordered by `Order`. Add business services to `ScopedBusinessServicesRegister`. Use typed `AddHttpClient<TInterface, TImpl>` — **don't** wrap with `AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>())`, which pins one `HttpClient` forever and defeats handler rotation.

### Read the matching rule before your first edit in an area

Not optional. Each rule file states invariants and traps that are not inferable from the surrounding code.

| Touching | Read (how to build) | Reference (what it does) |
|---|---|---|
| Any controller or DTO | `docs/rules/controllers.md` | `docs/reference/openapi.json` |
| Auth, JWT, whitelist | `docs/rules/auth.md` | `docs/reference/auth.md` |
| Ownership / membership / admin endpoints | `docs/rules/access-control.md` | `docs/reference/accounts.md` |
| Transactions, balance writes, categories | `docs/rules/transactions.md` | `docs/reference/transactions.md`, `categories.md` |
| Income/Expense shares | `docs/rules/transactions-shares.md` | `docs/reference/transactions.md` |
| Counterparty / Lend / Borrow / Obligations | `docs/rules/counterparty.md` | `docs/reference/counterparties.md` |
| EF queries, includes, migrations | `docs/rules/ef-pitfalls.md` | `docs/reference/data-model.md` |
| Any list GET endpoint | `docs/rules/pagination.md` | — |
| DTOs, enums, `DateTime`, OpenAPI | `docs/rules/serialization.md` | — |
| Tests | `docs/rules/testing.md` | — |
| `Program.cs` pipeline, Dockerfile, CI | `docs/rules/deployment.md` | `docs/guides/deployment.md` |
| Telegram webhook + `stock` grammar | — | `docs/reference/telegram-ingest.md` |

---

## Code Style

This section is resident because it applies to **every** edit. Formatting and naming are enforced by `.editorconfig` (2-space indent, Allman braces, 120-col, spaces inside parens, file-scoped namespaces, braces required, PascalCase / `_camelCase` / `I`-prefix / `Async`-suffix).

- **Primary constructors** (C# 12+) for DI: `public class Foo( ILogger logger ) { }`.
- **`field` keyword** (C# 14) for auto-properties needing setter logic — don't hand-roll a backing field. See `AppUser.Email`, `StockTransaction.Symbol`.
- **Extension members** (C# 14) — `extension( ClaimsPrincipal principal ) { ... }` blocks, not static `this`-parameter methods. See `ClaimsPrincipalExtensions`.
- **`record` for DTOs** — one class per file, file name matches class.
- **`var` when type is apparent.** `async`/`await` for all I/O — never `.Result`/`.Wait()`. Sync methods for non-I/O repo ops (`Attach`, `Update`, `Delete`).
- **XML docs:** only on public endpoint methods. Interface implementations use `/// <inheritdoc />`.
- **Money / quantity = `long`, not `decimal`.** VND has no fractional subunit; VN exchanges trade whole-share lots. Applies to balances, prices, totals, quantities — entity, DTO and parse layer alike. Only reintroduce `decimal` for a genuine sub-unit requirement (confirm first).

---

## Documentation

Four layers under `docs/`: **rules** (how to build) · **reference** (what it does) · **decisions** (why, historical) · **guides** (how to run it). Full contract and map: **`docs/README.md`**.

**On conflict, most authoritative first:** `docs/reference/openapi.json` + code → `docs/rules/*` → `docs/reference/*.md` → `docs/decisions/*` (never authoritative on current state).

`openapi.json` is generated — never hand-edit it, and never hand-write endpoint tables in prose.

---

## Workflow

- **After every code change:** call `mcp__ide__getDiagnostics` if available (else `dotnet build`), then run **only the test classes covering the files you touched** via `--filter`:
  ```
  dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~AuthControllerTests"
  ```
  Fall back to the full suite when the change is cross-cutting (`IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes) **or** as the pre-commit gate — all tests must pass before committing.

- **Update the doc layer that matches the change:**

  | Changed | Update |
  |---|---|
  | Endpoint added / removed / renamed, or a request record | regenerate `docs/reference/openapi.json` |
  | Behaviour | the matching `docs/reference/*.md` |
  | A project-wide pattern | `docs/rules/*` **and** this file (per `feedback_update_claude_md`) |
  | A decision made or reversed | a **new** `docs/decisions/NNNN-*.md` — never edit a merged ADR, supersede it |

  Nothing else needs touching. Adding a leaf service, a DTO or a test changes no doc.
