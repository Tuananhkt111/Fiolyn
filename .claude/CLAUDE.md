# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 8 web API that:
- Receives Telegram bot messages via webhook, checks the sender against an allowed-user whitelist (Neon PostgreSQL via EF Core), and saves the message as a new page in a Notion database.
- Parses stock transaction commands from Telegram messages (`stock buy/sell/div`) and persists them via a full CRUD REST API.

**Stack:** C# .NET 8 · Northflank (Docker) · GitHub Actions → GHCR · Neon PostgreSQL · Notion API · Telegram Bot API

---

## Commands

All commands run from the `Fiolyn/` subdirectory (where the `.csproj` lives):

```bash
dotnet run                                                        # run locally
dotnet ef migrations add <Name> --output-dir Data/Migrations      # new migration
dotnet ef database update                                         # apply migrations
```

Run tests from the repo root:

```bash
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj                      # run all tests
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --verbosity normal   # with output
```

Build/publish is handled by Docker — no manual `dotnet publish` needed locally.

---

## Local Configuration

Create `Fiolyn/appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "NeonDatabaseUrl": "postgresql://user:password@host/database?sslmode=require"
  },
  "TelegramBotToken": "your-telegram-bot-token",
  "NotionSetting": {
    "NotionApiKey": "your-notion-api-key",
    "NotionDatabaseId": "your-notion-database-id"
  }
}
```

**In production (Northflank)**, set these environment variables (nested keys use `__`):
```
CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
TELEGRAMBOTTOKEN=...
NOTIONSETTING__NOTIONAPIKEY=...
NOTIONSETTING__NOTIONDATABASEID=...
```

Configuration is bound to strongly-typed classes via `IOptions<T>` (see `ServiceRegisters/Settings/`).

See `docs/ConfigurationSettingSetup.md` for full details.

---

## Architecture

**Layered design**: Controller → Service (External) → Repository → DbContext → Database

```
POST /api/telegram
  ↓
TelegramController (thin orchestrator)
  ↓
ITelegramMessageService (External — webhook coordination)
  ├→ IRepository<TelegramUserInfo> (Generic — DB whitelist check + soft-delete filtering)
  ├→ IStockTransactionService (Internal — parses "stock …" messages, saves transactions)
  │    └→ IRepository<BrokerAccount> (Generic — broker lookup by name)
  └→ INotionService (External — Notion API integration)
  ↓
AppDbContext (Data — EF Core, auto-manages audit timestamps)
  ↓
PostgreSQL (Neon)

POST/GET/PUT/DELETE /api/stock-transactions
  ↓
StockTransactionController → IStockTransactionService → IRepository<StockTransaction>

POST/GET/PUT/DELETE /api/broker-accounts
  ↓
BrokerAccountController → IBrokerAccountService → IRepository<BrokerAccount>
```

### Architecture Patterns

**Service Layer**
- **External Services**: Call external systems (Telegram, Notion APIs)
- All services use **interface injection** for testability and decoupling
- Services throw on errors; `GlobalExceptionHandler` maps exceptions to RFC 7807 ProblemDetails responses
- **REST controllers** return proper HTTP status codes (201, 200, 204, 404) — only `TelegramController` always returns 200

**Generic Repository Pattern**
- `IRepository<T>` provides CRUD operations for any entity
- `QueryActive()` automatically filters soft-deleted entities
- `GetActiveByIdAsync(id)` returns null for soft-deleted entities (use for public-facing lookups)
- Registered globally via `RepositoriesServiceRegister`
- Eliminates need for entity-specific data access layers

**Audit Trail & Soft Deletes**
- Entities implement `IAuditable` for automatic timestamp management (`Created`, `LastModified`)
- Entities implement `ISoftDelete` for logical deletion (`IsDeleted` boolean)
- `AppDbContext.SetDefaultValues()` auto-populates audit fields on SaveChangesAsync()
- All timestamp operations use UTC to avoid timezone issues

### Files & Responsibilities

- `Program.cs` — top-level wiring (DI, middleware, migrations). No logic.
- `Infrastructure/GlobalExceptionHandler.cs` — maps exceptions to ProblemDetails (e.g., `KeyNotFoundException` → 404). Never leaks exception messages to clients.

**Services/External/Telegram/**
- `ITelegramMessageService.cs` — interface defining webhook processing contract
- `TelegramMessageService.cs` — implementation; orchestrates whitelist checks, stock parsing, and Notion persistence

**Services/Internal/**
- `IStockTransactionService.cs` — interface for stock transaction management + Telegram parsing
- `StockTransactionService.cs` — implementation; parses Telegram messages (`stock buy/sell/div`), looks up broker by name (case-insensitive), persists transactions
- `IBrokerAccountService.cs` — interface for broker account CRUD
- `BrokerAccountService.cs` — implementation

**Services/External/Notion/**
- `INotionService.cs` — interface for Notion API operations
- `NotionService.cs` — implementation; HTTP client integration (auth headers configured in `NotionServiceRegister`, not per-request)

**Controllers/**
- `TelegramController.cs` — HTTP orchestrator for webhook (always returns 200)
- `StockTransactionController.cs` — REST CRUD at `/api/stock-transactions` (proper HTTP status codes, response DTOs)
- `BrokerAccountController.cs` — REST CRUD at `/api/broker-accounts` (proper HTTP status codes, response DTOs)
- `Controllers/DTOs/Requests/` — request records (`BrokerAccountRequest`, `StockTransactionRequest`)
- `Controllers/DTOs/Responses/` — response records (`BrokerAccountResponse`, `StockTransactionResponse`)

**Data Access Layer**
- `Data/AppDbContext.cs` — EF Core DbContext; overrides SaveChangesAsync() to autopopulate audit fields
- `Data/Repositories/IRepository<T>.cs` — generic repository interface for all entities
- `Data/Repositories/Repository<T>.cs` — generic implementation; provides CRUD + soft-delete filtering
- `Data/Migrations/` — EF Core migrations (auto-generated, committed, applied on startup via `Migrate()`)

**Entity Model**
- `Entities/TelegramUserInfo.cs` — implements `IAuditable` and `ISoftDelete`
- `Entities/StockTransaction.cs` — implements `IAuditable` and `ISoftDelete`; references `BrokerAccount` via FK; `Symbol` setter auto-normalizes to uppercase via `NormalizeSymbol()`
- `Entities/BrokerAccount.cs` — implements `IAuditable` and `ISoftDelete`; name-keyed broker/account
- `Entities/Base/IAuditable.cs` — provides `Created` and `LastModified` timestamp properties
- `Entities/Base/ISoftDelete.cs` — provides `IsDeleted` boolean property

**DI & Configuration**
- `ServiceRegisters/` — DI registration via `IServiceRegister` interface (auto-discovered, ordered by `Order` property)
- `ServiceRegisters/Settings/` — strongly-typed config classes bound via `IOptions<T>`

---

## Code Style

- 2-space indentation, no tabs. Max 120 chars per line.
- Opening brace `{` on its own line for types, methods, and lambdas.
- Spaces inside all parentheses: `public class Foo( ILogger logger )`.
- `var` when type is obvious. `async`/`await` for all I/O — never `.Result`/`.Wait()`.
- **Primary constructors (C# 12) required** — reduces boilerplate. `public class Foo( ILogger logger ) { }`.
- `record` for DTOs. One class per file, file name matches class name.
- Throw on errors in services (don't silently fail). REST controllers use proper HTTP status codes; only `TelegramController` always returns 200.
- XML doc comments on public methods/endpoints only (suppress warnings with `NoWarn` in `.csproj`).
- Synchronous methods for non-I/O operations (e.g., `Attach()`, `Update()`, `Delete()` in repository are sync)

**Namespaces:**
- Controllers: `Fiolyn.Controllers`
- Request/Response DTOs: `Fiolyn.Controllers.DTOs.Requests.*` / `Fiolyn.Controllers.DTOs.Responses.*`
- Infrastructure: `Fiolyn.Infrastructure` (global exception handler)
- External services: `Fiolyn.Services.External` (Telegram, Notion API integrations)
- Internal services: `Fiolyn.Services.Internal` (business logic — stock transactions, broker accounts)
- Data repositories: `Fiolyn.Data.Repositories`
- Configuration classes: `Fiolyn.ServiceRegisters.Settings`
- Service registration: `Fiolyn.ServiceRegisters`
- Entities: `Fiolyn.Entities` and `Fiolyn.Entities.Base`
- Data: `Fiolyn.Data`
- `Program.cs` uses top-level statements (no namespace)

---

## Working with Entities

When creating new entities:

1. **Implement audit interface** for timestamp tracking:
   ```csharp
   public class MyEntity : IAuditable
   {
     [Key]
     public long Id { get; set; }
     
     [Required]
     public DateTime Created { get; set; }  // Auto-populated on insert
     
     [Required]
     public DateTime LastModified { get; set; }  // Auto-populated on insert/update
     
     // ... other properties
   }
   ```

2. **Implement soft-delete interface** for logical deletion:
   ```csharp
   public class MyEntity : IAuditable, ISoftDelete
   {
     // ... audit fields
     
     [Required]
     public bool IsDeleted { get; set; }  // Auto-set to false on insert
   }
   ```

3. **Use repository for all data access**:
   ```csharp
   public class MyService( IRepository<MyEntity> repository )
   {
     // Query only non-deleted entities
     var items = await repository.QueryActive().ToListAsync();
     
     // Soft-delete an entity
     await repository.SoftDelete(item);
     await repository.SaveAsync();
   }
   ```

---

## Working with Repositories

The generic `IRepository<T>` provides:

**Query Methods:**
- `Query()` — all entities (including soft-deleted)
- `QueryActive()` — only non-deleted entities (filters automatically if entity implements `ISoftDelete`)
- `GetByIdAsync(id)` — fetch by primary key (includes soft-deleted)
- `GetActiveByIdAsync(id)` — fetch by primary key, returns null if soft-deleted (use for public-facing lookups)

**Modification Methods:**
- `AddAsync(entity)` / `InsertRangeAsync(entities)` — add new entities
- `Attach(entity)` — attach detached entity without modifying DB
- `Update(entity)` / `UpdateRange(entities)` — mark entities as modified
- `Delete(entity)` — hard delete (removes from DB completely)
- `SoftDelete(entity)` — logical delete (sets `IsDeleted = true`)
- `Restore(entity)` — undo soft delete (sets `IsDeleted = false`)
- `SaveAsync()` — flush all pending changes to database

All modifications trigger `SetDefaultValues()` automatically, so audit timestamps are always current.

---

## Working with Migrations

- **Never edit migration files manually** — regenerate with `dotnet ef migrations add`
- **Never delete applied migrations** — create a new down migration if needed
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history
- **All migrations are committed to git** — schema changes are version-controlled

---

## Deployment

Push to `master` → GitHub Actions builds Docker image → pushes to GHCR → Northflank auto-deploys.

- **Dockerfile** at `Fiolyn/Dockerfile`; CI workflow specifies `file: Fiolyn/Dockerfile` with `context: .` (repo root)
- App listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`). Northflank terminates TLS externally
- **Do not use `UseHttpsRedirection`** — Northflank reverse proxy sends plain HTTP; HTTPS redirect causes failures
- **Secrets in Northflank env vars** (use `__` for nested keys):
  ```
  CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
  TELEGRAMBOTTOKEN=...
  NOTIONSETTING__NOTIONAPIKEY=...
  NOTIONSETTING__NOTIONDATABASEID=...
  ```
- **GitHub Actions secrets**: `NORTHFLANK_API_TOKEN` (for API deployments)

---

## Service Registration Pattern

Services are auto-discovered and registered via `IServiceRegister`:

```csharp
public class MyServiceRegister : IServiceRegister
{
  public int Order => 1 ;  // lower values run first
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<IMyService, MyService>() ;
  }
}
```

**Known registrars** (in order):
- `WebApiServiceRegister` (Order 0) — Controllers, Swagger, CORS, HTTP client factory, ProblemDetails + GlobalExceptionHandler, camelCase JSON
- `DatabaseServiceRegister` (Order 0) — DbContext and factory
- `RepositoriesServiceRegister` (Order 1) — Generic repository pattern
- `NotionServiceRegister` (Order 1) — Notion API configuration + named HTTP client (including Authorization header)
- `ScopedBusinessServicesRegister` (Order 1) — All scoped business services (Telegram, StockTransaction, BrokerAccount)

**Adding a new service:**
1. Create interface + implementation in `Services/External/` or `Services/Internal/`
2. Register in `ScopedBusinessServicesRegister` (for scoped business services) or create a new `IServiceRegister` if it needs custom configuration
3. No need to update `Program.cs` — auto-discovered

---

## Testing

### Project: `Fiolyn.Tests/`

**Framework:** xUnit 2.x · NSubstitute 5.x · FluentAssertions 6.x · EF Core InMemory · MockQueryable.NSubstitute

**Structure:**
```
Fiolyn.Tests/
├── GlobalUsings.cs                              # global using Xunit
├── Helpers/
│   └── MockHttpMessageHandler.cs               # captures HTTP request + body for assertions
├── Unit/
│   ├── Services/
│   │   ├── TelegramMessageServiceTests.cs      # 10 tests — webhook processing scenarios
│   │   ├── NotionServiceTests.cs               # 4 tests — HTTP payload, auth header, error handling
│   │   └── StockTransactionServiceTests.cs     # 9 tests — parse, broker lookup (case-insensitive), CRUD
│   └── Controllers/
│       └── TelegramControllerTests.cs          # 4 tests — always returns 200
└── Integration/
    ├── Helpers/
    │   ├── TestEntity.cs                       # minimal IAuditable + ISoftDelete entity for tests
    │   ├── TestDbContext.cs                    # extends AppDbContext, adds DbSet<TestEntity>
    │   └── TestDbContextFactory.cs            # creates isolated in-memory DB per test
    └── Data/
        ├── RepositoryTests.cs                  # 9 tests — soft delete, restore, query filtering, GetActiveByIdAsync
        └── AppDbContextTests.cs                # 2 tests — audit timestamp auto-population
```

### Unit Tests

- Mock all external dependencies (`IRepository<T>`, `INotionService`, `ITelegramMessageService`) with NSubstitute
- Use `MockQueryable` `BuildMock()` to make mocked `QueryActive()` work with EF Core async operators (`AnyAsync`, `ToListAsync`)
- Use `MockHttpMessageHandler` to test `NotionService` without real HTTP calls
- Use `DefaultHttpContext` to test `TelegramController` without spinning up a full web host

### Integration Tests

- Use `TestEntity` (not production entities) — decoupled from `TelegramUserInfo` or any future entity
- `TestDbContext` extends `AppDbContext` so audit logic (`SetDefaultValues`) is inherited automatically
- `TestDbContextFactory.Create()` uses `Guid.NewGuid()` as DB name for full test isolation
- Each test class implements `IDisposable` or `await using` to clean up DbContext

### Adding Tests for a New Entity or Service

**New service (unit test):**
1. Create `Unit/Services/MyServiceTests.cs`
2. Substitute all constructor dependencies with `Substitute.For<IMyDependency>()`
3. Use `BuildMock()` on a `List<T>` if the service calls `QueryActive()` on a repository

**New repository behavior (integration test):**
- `TestEntity` already implements `IAuditable` + `ISoftDelete` — use it directly in `RepositoryTests`
- No need to add entity-specific test cases; the generic tests already cover new entities

---

## Key Rules

- **Always return 200 from Telegram webhook** — Telegram retries non-2xx responses. Only `TelegramController` returns 200 on errors
- **REST controllers use proper HTTP status codes** — 201 Created, 200 OK, 204 NoContent, 404 NotFound. `GlobalExceptionHandler` catches unhandled exceptions and returns ProblemDetails
- **Services throw on failure** — don't silently fail. `GlobalExceptionHandler` maps exceptions to HTTP responses (e.g., `KeyNotFoundException` → 404)
- **Never leak exception messages to clients** — `GlobalExceptionHandler` logs the full exception but returns only the generic title in the ProblemDetails `Detail` field
- **Use primary constructors** — `public class Foo( ILogger logger )`
- **Use interface injection** — all services accept interfaces, not concrete implementations
- **Use generic repository** — never create entity-specific data access services (e.g., no `AllowedUsersRepository`)
- **Implement IAuditable on audit-trail entities** — timestamps are auto-managed
- **Implement ISoftDelete for logical deletion** — use `repository.QueryActive()` to exclude deleted records
- **Call `mcp__ide__getDiagnostics` after code changes** — before committing
- **Run `dotnet test` after code changes** — ensure all 44 tests still pass
- **DbSet property names are singular** — match the entity class name exactly (e.g. `StockTransaction`, `BrokerAccount`, not plurals)
- **`ParseMessageAsync` is async** — `IStockTransactionService.ParseMessageAsync` requires DB access to resolve `BrokerAccount` by name (case-insensitive); returns null if broker not found
- **Use `GetActiveByIdAsync` for public lookups** — returns null for soft-deleted entities. Use `GetByIdAsync` only when you need to access deleted entities (rare)
- **`StockTransaction.Symbol` auto-normalizes** — the setter calls `NormalizeSymbol()` (uppercase). No need to manually call `ToUpperInvariant()` on assignment
- **Use response DTOs in REST controllers** — never return raw entities. Use `record` types in `Controllers/DTOs/Responses/`
- **Use request DTOs for input validation** — `record` types in `Controllers/DTOs/Requests/` with `[Required]`/`[MaxLength]` annotations
- **camelCase JSON responses** — configured globally via `CamelCasePropertyNamesContractResolver` in `WebApiServiceRegister`
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history
- **Never edit migration files** — regenerate instead with `dotnet ef migrations add`
- **Never delete applied migrations** — create a new down migration if needed
