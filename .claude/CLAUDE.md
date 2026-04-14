# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 8 web API that receives Telegram bot messages via webhook, checks the sender against an allowed-user whitelist (Neon PostgreSQL via EF Core), and saves the message as a new page in a Notion database.

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
  └→ INotionService (External — Notion API integration)
  ↓
AppDbContext (Data — EF Core, auto-manages audit timestamps)
  ↓
PostgreSQL (Neon)
```

### Architecture Patterns

**Service Layer**
- **External Services**: Call external systems (Telegram, Notion APIs)
- All services use **interface injection** for testability and decoupling
- Services throw on errors; controllers handle gracefully and always return 200

**Generic Repository Pattern**
- `IRepository<T>` provides CRUD operations for any entity
- `QueryActive()` automatically filters soft-deleted entities
- Registered globally via `RepositoriesServiceRegister`
- Eliminates need for entity-specific data access layers

**Audit Trail & Soft Deletes**
- Entities implement `IAuditable` for automatic timestamp management (`Created`, `LastModified`)
- Entities implement `ISoftDelete` for logical deletion (`IsDeleted` boolean)
- `AppDbContext.SetDefaultValues()` auto-populates audit fields on SaveChangesAsync()
- All timestamp operations use UTC to avoid timezone issues

### Files & Responsibilities

- `Program.cs` — top-level wiring (DI, middleware, migrations). No logic.
- `Controllers/TelegramController.cs` — HTTP orchestrator only (parse body, delegate to service, return 200).

**Services/External/** — external system integrations
- `ITelegramMessageService.cs` — interface defining webhook processing contract
- `TelegramMessageService.cs` — implementation; orchestrates whitelist checks and Notion persistence
- `INotionService.cs` — interface for Notion API operations
- `NotionService.cs` — implementation; HTTP client integration with automatic header management

**Data Access Layer**
- `Data/AppDbContext.cs` — EF Core DbContext; overrides SaveChangesAsync() to auto-populate audit fields
- `Data/Repositories/IRepository<T>.cs` — generic repository interface for all entities
- `Data/Repositories/Repository<T>.cs` — generic implementation; provides CRUD + soft-delete filtering
- `Data/Migrations/` — EF Core migrations (auto-generated, committed, applied on startup via `Migrate()`)

**Entity Model**
- `Entities/TelegramUserInfo.cs` — implements `IAuditable` and `ISoftDelete`
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
- Throw on errors in services (don't silently fail). Controllers log and always return 200.
- XML doc comments on public methods/endpoints only (suppress warnings with `NoWarn` in `.csproj`).
- Synchronous methods for non-I/O operations (e.g., `Attach()`, `Update()`, `Delete()` in repository are sync)

**Namespaces:**
- Controllers: `Fiolyn.Controllers`
- External services: `Fiolyn.Services.External` (API integrations)
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
- `GetByIdAsync(id)` — fetch by primary key

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

**Create a new migration:**
```bash
dotnet ef migrations add MigrationName --output-dir Data/Migrations
```

**Apply pending migrations to database:**
```bash
dotnet ef database update
```

**Key rules:**
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
- `WebApiServiceRegister` (Order 0) — Controllers, Swagger, CORS, HTTP client factory
- `DatabaseServiceRegister` (Order 0) — DbContext and factory
- `RepositoriesServiceRegister` (Order 1) — Generic repository pattern
- `NotionServiceRegister` (Order 1) — Notion API configuration + named HTTP client
- `ScopedServicesRegister` (Order 1) — Business logic services

**Adding a new service:**
1. Create interface + implementation in `Services/External/` or `Services/Internal/`
2. Create new `IServiceRegister` in `ServiceRegisters/`
3. No need to update `Program.cs` — auto-discovered

---

## Testing

### Project: `Fiolyn.Tests/`

**Framework:** xUnit 2.x · NSubstitute 5.x · FluentAssertions 6.x · EF Core InMemory · MockQueryable.NSubstitute

**Structure:**
```
Fiolyn.Tests/
├── GlobalUsings.cs                          # global using Xunit
├── Helpers/
│   └── MockHttpMessageHandler.cs           # captures HTTP request + body for assertions
├── Unit/
│   ├── Services/
│   │   ├── TelegramMessageServiceTests.cs  # 8 tests — webhook processing scenarios
│   │   └── NotionServiceTests.cs           # 4 tests — HTTP payload, auth header, error handling
│   └── Controllers/
│       └── TelegramControllerTests.cs      # 4 tests — always returns 200
└── Integration/
    ├── Helpers/
    │   ├── TestEntity.cs                   # minimal IAuditable + ISoftDelete entity for tests
    │   ├── TestDbContext.cs                # extends AppDbContext, adds DbSet<TestEntity>
    │   └── TestDbContextFactory.cs        # creates isolated in-memory DB per test
    └── Data/
        ├── RepositoryTests.cs              # 6 tests — soft delete, restore, query filtering
        └── AppDbContextTests.cs            # 2 tests — audit timestamp auto-population
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

- **Always return 200 from Telegram webhook** — Telegram retries non-2xx responses. Controller returns 200 even on errors
- **Services throw on failure** — don't silently fail. Let controller handle and log
- **Use primary constructors** — `public class Foo( ILogger logger )`
- **Use interface injection** — all services accept interfaces, not concrete implementations
- **Use generic repository** — never create entity-specific data access services (e.g., no `AllowedUsersRepository`)
- **Implement IAuditable on audit-trail entities** — timestamps are auto-managed
- **Implement ISoftDelete for logical deletion** — use `repository.QueryActive()` to exclude deleted records
- **Call `mcp__ide__getDiagnostics` after code changes** — before committing
- **Run `dotnet test` after code changes** — ensure all 24 tests still pass
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history
- **Never edit migration files** — regenerate instead with `dotnet ef migrations add`
- **Never delete applied migrations** — create a new down migration if needed
