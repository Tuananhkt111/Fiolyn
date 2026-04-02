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

**Layered design**: Controller → Services (External + Internal) → Data Access

```
POST /api/telegram
  ↓
TelegramController (thin orchestrator)
  ↓
TelegramMessageService (External — webhook coordination)
  ├→ AllowedUsersService (Internal — DB whitelist check)
  └→ NotionService (External — Notion API integration)
  ↓
AppDbContext (Data — EF Core)
```

Services are organized by responsibility:
- **External**: Services that call external systems (APIs, webhooks)
- **Internal**: Services that handle internal business logic (DB queries, validation)

### Files & Responsibilities

- `Program.cs` — top-level wiring (DI, middleware, migrations). No logic.
- `Controllers/TelegramController.cs` — HTTP orchestrator only (parse body, delegate to service, return 200).

**Services/External/** — external system integrations
- `TelegramMessageService.cs` — coordinates webhook processing and external calls.
- `NotionService.cs` — Notion API integration (HTTP client via `IHttpClientFactory`). Throws on errors.

**Services/Internal/** — internal business logic
- `AllowedUsersService.cs` — encapsulates allowed-user whitelist checks via database.

**DI & Configuration**
- `ServiceRegisters/` — DI registration via `IServiceRegister` interface (auto-discovered, ordered by `Order` property).
- `ServiceRegisters/Settings/` — strongly-typed config classes bound via `IOptions<T>`.

**Data & Persistence**
- `Data/AppDbContext.cs` + `Entities/AllowedUser.cs` — EF Core Code First; schema in C#, never raw SQL.
- `Data/Migrations/` — auto-generated, committed, applied on startup via `Migrate()`.

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

**Namespaces:**
- Controllers: `Fiolyn.Controllers`
- External services: `Fiolyn.Services.External` (API integrations, webhooks)
- Internal services: `Fiolyn.Services.Internal` (business logic, DB queries)
- Configuration classes: `Fiolyn.ServiceRegisters.Settings`
- Service registration: `Fiolyn.ServiceRegisters`
- Entities: `Fiolyn.Entities`
- Data: `Fiolyn.Data`
- `Program.cs` uses top-level statements (no namespace).

---

## Deployment

Push to `master` → GitHub Actions builds Docker image → pushes to GHCR → Northflank auto-deploys.

- **Dockerfile** at `Fiolyn/Dockerfile`; CI workflow specifies `file: Fiolyn/Dockerfile` with `context: .` (repo root).
- App listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`). Northflank terminates TLS externally.
- **Do not use `UseHttpsRedirection`** — Northflank reverse proxy sends plain HTTP; HTTPS redirect causes failures.
- **Secrets in Northflank env vars** (use `__` for nested keys):
  ```
  CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
  TELEGRAMBOTTOKEN=...
  NOTIONSETTING__NOTIONAPIKEY=...
  NOTIONSETTING__NOTIONDATABASEID=...
  ```
- **GitHub Actions secrets**: `NORTHFLANK_API_TOKEN` (for API deployments).

---

## Service Registration Pattern

Services are auto-discovered and registered via `IServiceRegister`:

```csharp
public class MyServiceRegister : IServiceRegister
{
  public int Order => 1 ;  // lower values run first
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<MyService>() ;
  }
}
```

**Known registrars** (in order):
- `WebApiServiceRegister` (Order 0) — Controllers, Swagger, CORS, HTTP client factory
- `DatabaseServiceRegister` — DbContext and factory
- `NotionServiceRegister` — Notion API configuration
- `ScopedServicesRegister` (Order 1) — Business logic services

## Key Rules

- **Always return 200 from Telegram webhook** — Telegram retries non-2xx responses. Controller returns 200 even on errors.
- **Services throw on failure** — don't silently fail. Let controller handle and log.
- **Use primary constructors** — `public class Foo( ILogger logger )`.
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history.
- **Never edit migration files** — regenerate instead with `dotnet ef migrations add`.
- **Never delete applied migrations** — create a new down migration if needed.
- **Call `mcp__ide__getDiagnostics` after code changes** — before committing.