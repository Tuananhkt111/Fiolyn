# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 10 web API that:
- Receives Telegram bot messages via webhook, checks the sender against an allowed-user whitelist (Neon PostgreSQL via EF Core), and saves the message as a new page in a Notion database.
- Parses stock transaction commands from Telegram messages (`stock buy/sell/div`) and persists them via a full CRUD REST API.
- Authenticates API callers (the future mobile stock-management app) via Google SSO; issues Fiolyn-signed JWT access + rotating refresh tokens gated on an admin-managed email whitelist.

**Stack:** C# 14 on .NET 10 (LTS) · Northflank (Docker) · GitHub Actions → GHCR · Neon PostgreSQL · Notion API · Telegram Bot API · Google Sign-In (ID token) · EF Core 10 · Swashbuckle 10 · xUnit 2 · NSubstitute 5 · FluentAssertions 6

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

`Fiolyn/appsettings.json` is tracked and intentionally minimal — only `Logging` + `AllowedHosts`. **No secrets, connection strings, or auth placeholders live in it.** All environment-specific config lives in one of two places:

- **Local dev**: `Fiolyn/appsettings.Development.json` (gitignored) — or the repo-root `.env` file (also gitignored, read by the runtime via `DOTNET_` env-var conventions).
- **Production**: Northflank env vars with `__` for nested keys.

Create `Fiolyn/appsettings.Development.json` (gitignored) with all the local values:

```json
{
  "ConnectionStrings": {
    "NeonDatabaseUrl": "postgresql://user:password@host/database?sslmode=require"
  },
  "TelegramBotToken": "your-telegram-bot-token",
  "NotionSettings": {
    "NotionApiKey": "your-notion-api-key",
    "NotionDatabaseId": "your-notion-database-id"
  },
  "Jwt": {
    "Issuer": "Fiolyn",
    "Audience": "FiolynMobile",
    "SigningKey": "<random base64 or hex, >= 32 chars — generate with `openssl rand -base64 48`>",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 30
  },
  "GoogleAuth": {
    "ClientId": "<your-google-oauth-client-id>.apps.googleusercontent.com"
  }
}
```

- `Jwt:SigningKey` must be ≥32 characters — the app **fails fast at startup** otherwise (both `AuthServiceRegister` and `JwtService.ctor` throw `InvalidOperationException`). No silent dev fallback.
- `GoogleAuth:ClientId` is the OAuth 2.0 Client ID from Google Cloud Console — it's the expected `aud` claim when validating incoming Google ID tokens. No client secret needed for Flow 1A (mobile SDK → ID token).

**In production (Northflank)**, set these environment variables (nested keys use `__`):
```
CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
TELEGRAMBOTTOKEN=...
NOTIONSETTINGS__NOTIONAPIKEY=...
NOTIONSETTINGS__NOTIONDATABASEID=...
JWT__SIGNINGKEY=<random, >= 32 chars>
JWT__ISSUER=Fiolyn
JWT__AUDIENCE=FiolynMobile
GOOGLEAUTH__CLIENTID=<google-oauth-client-id>.apps.googleusercontent.com
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

POST/GET/PUT/DELETE /api/stock-transactions      (class-level [Authorize])
  ↓
StockTransactionController → IStockTransactionService → IRepository<StockTransaction>

POST/GET/PUT/DELETE /api/broker-accounts         (class-level [Authorize])
  ↓
BrokerAccountController → IBrokerAccountService → IRepository<BrokerAccount>

POST /api/auth/{google|refresh|logout}
  ↓
AuthController
  ↓
IAuthService (Internal — orchestration)
  ├→ IGoogleTokenVerifier (External — validates Google ID token via Google.Apis.Auth)
  ├→ IJwtService (Internal — HS256 access token + opaque rotating refresh token)
  ├→ IRepository<AllowedEmail> (whitelist gate)
  ├→ IRepository<AppUser> (upsert on first login)
  └→ IRepository<RefreshToken> (rotation chain + reuse detection)

GET/PATCH/DELETE /api/users (admin endpoints) and /api/users/me (self-service)
  ↓
UserController → IUserService → IRepository<AppUser>

GET/POST/DELETE /api/allowed-emails              (admin only, Policy="Admin")
  ↓
AllowedEmailController → IAllowedEmailService → IRepository<AllowedEmail>
```

### Architecture Patterns

**Service Layer**
- **External Services**: Call external systems (Telegram, Notion APIs)
- All services use **interface injection** for testability and decoupling
- Services throw on errors; `GlobalExceptionHandler` maps exceptions to RFC 7807 ProblemDetails responses (`KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException` → 403, everything else → 500)
- **REST controllers** return proper HTTP status codes (201, 200, 204, 401, 403, 404) — only `TelegramController` always returns 200

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

**Authentication (Google SSO + Fiolyn JWT)**
- **Login**: mobile client obtains a Google ID token via Google Sign-In SDK and POSTs it to `POST /api/auth/google`. Backend verifies the ID token with `Google.Apis.Auth`, enforces the `AllowedEmail` whitelist, upserts an `AppUser` row (single merged lookup by `GoogleSub` OR `Email`), and issues a Fiolyn-signed JWT access token + opaque refresh token.
- **Refresh**: `POST /api/auth/refresh` rotates the refresh token (stored as SHA-256 hash). The INSERT of the new token and the UPDATE revoking the old one flush in a single `SaveChangesAsync` — one EF transaction, so rotation is atomic. Replaying a revoked token revokes the entire rotation chain for that user (reuse-detection).
- **Logout**: `POST /api/auth/logout` sets `RevokedAt` on the presented refresh token.
- **Authorization model**: `Authenticated` (default) vs `Admin` policy (requires `isAdmin=true` claim). Protected by class-level `[Authorize]` on `StockTransactionController` / `BrokerAccountController` / `UserController`; `[Authorize(Policy = AuthConstants.AdminPolicyName)]` on `AllowedEmailController` and admin-only endpoints on `UserController`. `TelegramController` is `[AllowAnonymous]` (webhook must accept unauthenticated).
- **Admin bootstrap**: the first admin is created manually — `INSERT` an `AllowedEmail` row for that email, let them log in once (Google creates the `AppUser`), then `UPDATE` `AppUser.IsAdmin = true` for that row. No code path auto-promotes anyone.
- **Admin whitelist management**: `/api/allowed-emails` (GET/POST/DELETE) and `/api/users` (GET/PATCH/DELETE) — all admin-only.
- **Settings**: `Jwt` (`SigningKey` ≥32 chars HS256 — **required, app fails fast at startup otherwise**; 15-min access / 30-day refresh by default), `GoogleAuth:ClientId` (no client secret needed).
- **Magic strings**: claim/policy names live in `Fiolyn/Constants/AuthConstants.cs` — `AdminClaimName` (`"isAdmin"`), `AdminClaimTrueValue` (`"true"`), `AdminPolicyName` (`"Admin"`). Never hard-code these.
- **Extracting the caller**: inject nothing — use the `User.GetUserId()` / `User.TryGetUserId()` extensions from `Fiolyn.Extensions.ClaimsPrincipalExtensions` inside any controller. They read the JWT `sub` claim.

### Files & Responsibilities

- `Program.cs` — top-level wiring (DI, middleware). Calls `app.InitializeDatabaseAsync()` before `app.Run()`. No business logic.
- `Infrastructure/GlobalExceptionHandler.cs` — maps exceptions to ProblemDetails (`KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException` → 403, everything else → 500). Never leaks exception messages to clients.
- `Infrastructure/Exceptions/ForbiddenAccessException.cs` — custom exception signalling "authenticated but not authorized" (maps to 403).
- `Constants/AuthConstants.cs` — shared auth string constants (`AdminClaimName`, `AdminClaimTrueValue`, `AdminPolicyName`). Referenced by `JwtService`, `AuthServiceRegister`, and both admin controllers.
- `Extensions/ClaimsPrincipalExtensions.cs` — `User.GetUserId()` (throws) and `User.TryGetUserId()` (returns `long?`) for reading the JWT `sub` claim inside any controller.

**Services/External/Telegram/**
- `ITelegramMessageService.cs` / `TelegramMessageService.cs` — webhook coordinator; orchestrates whitelist checks, stock parsing, and Notion persistence.

**Services/External/Notion/**
- `INotionService.cs` / `NotionService.cs` — Notion API integration; HTTP client integration (auth headers configured in `NotionServiceRegister`, not per-request).

**Services/External/Google/**
- `IGoogleTokenVerifier.cs` / `GoogleTokenVerifier.cs` — wraps `GoogleJsonWebSignature.ValidateAsync` to verify Google ID tokens against the configured `GoogleAuth:ClientId`. Returns `GooglePayload(Email, Sub, Name, Picture)` or throws `UnauthorizedAccessException`. Throws `InvalidOperationException` (→ 500) if `ClientId` is missing — deployment error, not auth error.

**Services/Internal/** (implementations) and **Services/Internal/Interfaces/** (interfaces)
- `IStockTransactionService.cs` / `StockTransactionService.cs` — stock transaction CRUD + Telegram message parsing (`stock buy/sell/div`); uses `.Include(t => t.BrokerAccount)` to eager-load the navigation property.
- `IBrokerAccountService.cs` / `BrokerAccountService.cs` — broker account CRUD.
- `IAuthService.cs` / `AuthService.cs` — Google SSO login (verify → whitelist check → upsert AppUser → issue tokens), refresh with rotation + reuse detection, logout. Uses `AppUser.NormalizeEmail()` for casing consistency. The private `IssueTokensAsync` *stages* the new `RefreshToken` via `AddAsync` but does **not** call `SaveAsync` — each caller (`LoginWithGoogleAsync`, `RefreshAsync`) flushes once at the end so user upsert + token insert (+ token revocation for refresh) land in one transaction.
- `IJwtService.cs` / `JwtService.cs` — pure token plumbing: issues signed access tokens (HS256) and opaque refresh tokens (64 random bytes → base64). Caches `JwtSecurityTokenHandler` statically; caches `_signingKeyBytes` at construction. Constructor validates `SigningKey` length — **throws `InvalidOperationException` if key is absent or <32 chars** (defence-in-depth alongside the startup check).
- `IAllowedEmailService.cs` / `AllowedEmailService.cs` — admin CRUD over the `AllowedEmail` whitelist.
- `IUserService.cs` / `UserService.cs` — self-service `GetByIdAsync` / `UpdateDisplayNameAsync`; admin `GetAllAsync` / `SetAdminAsync` / `SoftDeleteAsync`.

**Controllers/**
- `TelegramController.cs` — HTTP orchestrator for webhook; `[AllowAnonymous]`, always returns 200.
- `StockTransactionController.cs` — REST CRUD at `/api/stock-transactions`; class-level `[Authorize]`.
- `BrokerAccountController.cs` — REST CRUD at `/api/broker-accounts`; class-level `[Authorize]`.
- `AuthController.cs` — `/api/auth/google` (login), `/api/auth/refresh`, `/api/auth/logout`; first two are `[AllowAnonymous]`, logout is `[Authorize]`.
- `UserController.cs` — class-level `[Authorize]` with admin endpoints decorated `[Authorize(Policy = AuthConstants.AdminPolicyName)]`. Reads caller ID via `User.GetUserId()`.
- `AllowedEmailController.cs` — class-level `[Authorize(Policy = AuthConstants.AdminPolicyName)]`. Reads inviter ID via `User.TryGetUserId()`.
- `Controllers/DTOs/Requests/` — request records grouped per resource: `Auth/{GoogleLoginRequest, RefreshTokenRequest}`, `AllowedEmail/AllowedEmailRequest`, `User/{UpdateMeRequest, UpdateRoleRequest}`, plus existing `StockTransaction` and `BrokerAccount`.
- `Controllers/DTOs/Responses/` — response records: `Auth/AuthResponse`, `User/UserResponse`, `AllowedEmail/AllowedEmailResponse`, plus existing ones. `StockTransactionResponse` includes `BrokerAccountName` via eager-loaded navigation property — `StockTransactionController.CreateAsync` re-fetches through `GetByIdAsync` after persisting so the navigation is populated (the freshly-added entity has only `BrokerAccountId`, not the nav reference).

**Data Access Layer**
- `Data/AppDbContext.cs` — EF Core DbContext; overrides `SaveChangesAsync()` to autopopulate audit fields. DbSets: `TelegramUserInfo`, `BrokerAccount`, `StockTransaction`, `AppUser`, `AllowedEmail`, `RefreshToken`.
- `Data/Repositories/IRepository<T>.cs` / `Repository<T>.cs` — generic CRUD + soft-delete filtering.
- `Data/Migrations/` — EF Core migrations (auto-generated, committed, applied on startup via `MigrateAsync()` inside `DatabaseInitializer`).
- `Data/Seeds/DatabaseInitializer.cs` — exposes `WebApplication.InitializeDatabaseAsync()` extension: runs `MigrateAsync()`.

**Entity Model**
- `Entities/TelegramUserInfo.cs` — implements `IAuditable` + `ISoftDelete`.
- `Entities/StockTransaction.cs` — implements `IAuditable` + `ISoftDelete`; `Symbol` setter auto-normalizes to uppercase via `NormalizeSymbol()`.
- `Entities/BrokerAccount.cs` — implements `IAuditable` + `ISoftDelete`; name-keyed broker.
- `Entities/AppUser.cs` — implements `IAuditable` + `ISoftDelete`; `[Index]` attributes on `Email` + `GoogleSub` (both unique). Setter on `Email` auto-lowercases via static `NormalizeEmail()`. Nav `ICollection<RefreshToken>`.
- `Entities/AllowedEmail.cs` — implements `IAuditable` + `ISoftDelete`; unique index on `Email`; setter auto-lowercases via `AppUser.NormalizeEmail()`. Optional `InvitedByUserId` FK → `AppUser`.
- `Entities/RefreshToken.cs` — implements `IAuditable` only (revocation is explicit via `RevokedAt`, not soft-delete). FK → `AppUser` (Cascade, inferred by EF convention). Stores SHA-256 `TokenHash`, never raw tokens. `ReplacedByTokenHash` carries the rotation chain for reuse-detection.
- `Entities/Base/IAuditable.cs` — `Created` + `LastModified`.
- `Entities/Base/ISoftDelete.cs` — `IsDeleted` boolean.

**DI & Configuration**
- `ServiceRegisters/` — DI registration via `IServiceRegister` interface (auto-discovered, ordered by `Order` property).
- `ServiceRegisters/Settings/` — strongly-typed config classes bound via `IOptions<T>`: `NotionSettings`, `JwtSettings`, `GoogleAuthSettings`.

---

## Code Style

- 2-space indentation, no tabs. Max 120 chars per line.
- Opening brace `{` on its own line for types, methods, and lambdas.
- Spaces inside all parentheses: `public class Foo( ILogger logger )`.
- `var` when type is obvious. `async`/`await` for all I/O — never `.Result`/`.Wait()`.
- **Primary constructors required** (C# 12+, standard since .NET 8) — reduces boilerplate. `public class Foo( ILogger logger ) { }`.
- **Use the `field` contextual keyword** (C# 14) for auto-properties that need setter logic (normalization, validation) — don't hand-roll a private backing field. Example pattern: `public string Email { get; set => field = NormalizeEmail( value ) ; } = string.Empty ;`. Already applied to `AppUser.Email`, `AllowedEmail.Email`, `StockTransaction.Symbol`.
- **Extension members** (C# 14) — if you add read-only helpers over `ClaimsPrincipal` / `HttpContext` / etc., prefer an `extension( ClaimsPrincipal principal )` block over a static class full of `this`-parameter methods. `ClaimsPrincipalExtensions` already uses the `extension()` block — follow that pattern for new extensions.
- **Collection expressions** (`[ ]` syntax, C# 12) for array/list literals. Don't use `new[] { ... }` or `new List<T> { ... }` in new code.
- `record` for DTOs. One class per file, file name matches class name.
- Throw on errors in services (don't silently fail). REST controllers use proper HTTP status codes; only `TelegramController` always returns 200.
- XML doc comments on public methods/endpoints only (suppress warnings with `NoWarn` in `.csproj`).
- Synchronous methods for non-I/O operations (e.g., `Attach()`, `Update()`, `Delete()` in repository are sync)

**Namespaces:**
- Controllers: `Fiolyn.Controllers`
- Request/Response DTOs: `Fiolyn.Controllers.DTOs.Requests.*` / `Fiolyn.Controllers.DTOs.Responses.*` — grouped per resource (`.Auth`, `.User`, `.AllowedEmail`, `.StockTransaction`, `.BrokerAccount`)
- Constants: `Fiolyn.Constants` (e.g. `AuthConstants`)
- Extensions: `Fiolyn.Extensions` (e.g. `ClaimsPrincipalExtensions`)
- Infrastructure: `Fiolyn.Infrastructure` (`GlobalExceptionHandler`) and `Fiolyn.Infrastructure.Exceptions` (custom exception types)
- External services: `Fiolyn.Services.External.{Telegram,Notion,Google}`
- Internal service implementations: `Fiolyn.Services.Internal`
- Internal service interfaces: `Fiolyn.Services.Internal.Interfaces`
- Data repositories: `Fiolyn.Data.Repositories`
- Data seeding / startup: `Fiolyn.Data.Seeds` (`DatabaseInitializer`)
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
- `GetActiveByIdAsync(id)` — fetch by primary key, returns null if soft-deleted (use when you want to handle the "missing" case yourself)
- `GetActiveByIdOrThrowAsync(id)` — same as above, but throws `KeyNotFoundException` (→ HTTP 404 via `GlobalExceptionHandler`) on miss. Preferred for `SoftDeleteAsync`/`UpdateAsync` paths where "not found" is an error.

**Modification Methods:**
- `AddAsync(entity)` / `InsertRangeAsync(entities)` — add new entities
- `Attach(entity)` — attach detached entity without modifying DB
- `Update(entity)` / `UpdateRange(entities)` — mark entities as modified
- `Delete(entity)` — hard delete (removes from DB completely)
- `SoftDelete(entity)` — logical delete (sets `IsDeleted = true`)
- `Restore(entity)` — undo soft delete (sets `IsDeleted = false`)
- `SaveAsync()` — flush all pending changes to database

All modifications trigger `SetDefaultValues()` automatically, so audit timestamps are always current.

**Read-only list paths use `.AsNoTracking()`.** When a service's method returns a `List<T>` that the controller projects straight to a DTO without mutating the entities, chain `.AsNoTracking()` before `.ToListAsync()`. Applied in every `GetAllAsync` on `UserService`, `AllowedEmailService`, `BrokerAccountService`, `StockTransactionService` — skips change-tracker snapshots and lowers per-request memory. Do **not** add `.AsNoTracking()` to `GetByIdAsync` paths, because some controllers fetch by id, mutate, then call `UpdateAsync` on the same entity.

---

## Working with Migrations

- **Never edit migration files manually** — regenerate with `dotnet ef migrations add`
- **To undo the most recent *unapplied* migration** — use `dotnet ef migrations remove` (it deletes the `.cs`/`.Designer.cs` files and reverts the model snapshot). Use this after an entity rename when the in-flight migration is stale; do **not** delete the files by hand.
- **Never delete applied migrations** — create a new down migration if needed
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history
- **All migrations are committed to git** — schema changes are version-controlled
- Migrations are applied on app startup via `Database.MigrateAsync()` inside `Data/Seeds/DatabaseInitializer.InitializeDatabaseAsync()`, which `Program.cs` awaits before `app.Run()`.

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
  NOTIONSETTINGS__NOTIONAPIKEY=...
  NOTIONSETTINGS__NOTIONDATABASEID=...
  JWT__SIGNINGKEY=<random, >= 32 chars>
  JWT__ISSUER=Fiolyn
  JWT__AUDIENCE=FiolynMobile
  GOOGLEAUTH__CLIENTID=<google-oauth-client-id>.apps.googleusercontent.com
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
- `DatabaseServiceRegister` (Order 0) — DbContext and factory. Exposes `app.InitializeDatabaseAsync()` extension that runs migrations (invoked from `Program.cs`).
- `RepositoriesServiceRegister` (Order 1) — Generic repository pattern
- `NotionServiceRegister` (Order 1) — Notion API configuration via `AddHttpClient<INotionService, NotionService>` (typed client; `IHttpClientFactory` owns handler rotation). Authorization header is configured once at registration, not per-request
- `AuthServiceRegister` (Order 1) — JWT bearer scheme, `Admin` authorization policy, `Jwt`/`GoogleAuth` settings binding, and the auth plumbing services (`IGoogleTokenVerifier`, `IJwtService`)
- `ScopedBusinessServicesRegister` (Order 1) — All scoped business services (Telegram, StockTransaction, BrokerAccount, Auth, User, AllowedEmail)

**Adding a new service:**
1. Create the interface in `Services/Internal/Interfaces/IFooService.cs` (or `Services/External/Foo/IFooService.cs` for external integrations) and the implementation in `Services/Internal/FooService.cs` (or `Services/External/Foo/FooService.cs`)
2. Register in `ScopedBusinessServicesRegister` (for scoped business services) or create a new `IServiceRegister` if it needs custom configuration (e.g. `AuthServiceRegister` for auth plumbing)
3. No need to update `Program.cs` — auto-discovered

---

## Testing

### Project: `Fiolyn.Tests/`

**Framework:** xUnit 2.x · NSubstitute 5.x · FluentAssertions 6.x · EF Core InMemory · MockQueryable.NSubstitute

**Structure (73 tests total):**
```
Fiolyn.Tests/
├── GlobalUsings.cs                              # global using Xunit
├── Helpers/
│   └── MockHttpMessageHandler.cs               # captures HTTP request + body for assertions
├── Unit/
│   ├── Services/
│   │   ├── TelegramMessageServiceTests.cs      # webhook processing scenarios
│   │   ├── NotionServiceTests.cs               # HTTP payload, auth header, error handling
│   │   ├── StockTransactionServiceTests.cs     # parse, broker lookup (case-insensitive), CRUD
│   │   ├── JwtServiceTests.cs                  # access token claims, expiry, refresh uniqueness; ctor fails fast on missing/short SigningKey
│   │   ├── GoogleTokenVerifierTests.cs         # guard paths (empty token, missing ClientId, malformed JWT)
│   │   ├── AuthServiceTests.cs                 # whitelist enforcement, first-login user creation, refresh rotation, reuse-detection chain revoke, logout
│   │   ├── AllowedEmailServiceTests.cs         # create (normalizes, dedupes), soft-delete
│   │   └── UserServiceTests.cs                 # profile updates, admin toggle, soft-delete
│   └── Controllers/
│       ├── TelegramControllerTests.cs          # always returns 200
│       └── AuthControllerTests.cs              # login/refresh/logout status codes
└── Integration/
    ├── Helpers/
    │   ├── TestEntity.cs                       # minimal IAuditable + ISoftDelete entity for tests
    │   ├── TestDbContext.cs                    # extends AppDbContext, adds DbSet<TestEntity>
    │   └── TestDbContextFactory.cs            # creates isolated in-memory DB per test
    └── Data/
        ├── RepositoryTests.cs                  # soft delete, restore, query filtering, GetActiveByIdAsync
        └── AppDbContextTests.cs                # audit timestamp auto-population
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
- **Run `dotnet test` after code changes** — ensure all 73 tests still pass
- **DbSet property names are singular** — match the entity class name exactly (e.g. `StockTransaction`, `BrokerAccount`, not plurals)
- **`ParseMessageAsync` is async** — `IStockTransactionService.ParseMessageAsync` requires DB access to resolve `BrokerAccount` by name (case-insensitive); returns null if broker not found
- **Use `GetActiveByIdOrThrowAsync` when "not found" is an error** — it throws `KeyNotFoundException` (→ 404 via `GlobalExceptionHandler`) so services don't each reinvent the same `?? throw new KeyNotFoundException(...)` line. Use `GetActiveByIdAsync` only when the caller needs to branch on null. Use `GetByIdAsync` only when you need access to soft-deleted records (rare).
- **`StockTransaction.Symbol` auto-normalizes** — the setter calls `NormalizeSymbol()` (uppercase). No need to manually call `ToUpperInvariant()` on assignment
- **Use response DTOs in REST controllers** — never return raw entities. Use `record` types in `Controllers/DTOs/Responses/`
- **Use request DTOs for input validation** — `record` types in `Controllers/DTOs/Requests/` with `[Required]`/`[MaxLength]` annotations
- **camelCase JSON responses** — configured globally via `CamelCasePropertyNamesContractResolver` in `WebApiServiceRegister`
- **Never use `EnsureCreated()`** — bypasses migrations, corrupts schema history
- **Never edit migration files** — regenerate instead with `dotnet ef migrations add`
- **Never delete applied migrations** — create a new down migration if needed
- **Interfaces live in `Services/Internal/Interfaces/`** — pair each `IFooService.cs` interface with a `Services/Internal/FooService.cs` implementation
- **Put service interfaces + implementations in matching namespaces** — `Fiolyn.Services.Internal.Interfaces` for the interface, `Fiolyn.Services.Internal` for the implementation. Controllers inject interfaces via `using Fiolyn.Services.Internal.Interfaces ;`
- **Auth claim/policy strings come from `AuthConstants`** — never hardcode `"Admin"` / `"isAdmin"` / `"true"` in attributes or policy config. Use `AuthConstants.AdminPolicyName` / `AdminClaimName` / `AdminClaimTrueValue`
- **`Jwt:SigningKey` is mandatory** — both `AuthServiceRegister` (startup) and `JwtService` ctor throw `InvalidOperationException` if it's missing or <32 chars. There is no silent dev fallback
- **`AppUser.NormalizeEmail(email)`** — the canonical email normalizer (lowercases + trims). Called by `AppUser.Email` / `AllowedEmail.Email` setters and by `AuthService`. Don't roll your own
- **Read caller ID via `User.GetUserId()` / `User.TryGetUserId()`** — extensions in `Fiolyn.Extensions.ClaimsPrincipalExtensions`. Don't reach for `ClaimsPrincipal.FindFirst(...)` directly in controllers
- **`AppUser` (entity) ≠ `User` (controller `ClaimsPrincipal`)** — inside a controller, `User` is the caller's identity. Reference the entity as `AppUser` (or fully-qualified `Entities.AppUser`) to avoid collision
- **Refresh tokens never leave `JwtService.CreateRefreshToken()` raw twice** — the raw value is returned to the client once, and only its SHA-256 hash is persisted. Reuse detection relies on `ReplacedByTokenHash` forming a rotation chain
- **Seeders use `IRepository<T>`** — not `AppDbContext` DbSets directly. The repository's `QueryActive()` is the one place that filters soft-deleted records
- **Chain `.AsNoTracking()` on read-only list queries** — if a `List<T>` is being projected to DTOs and never mutated, skipping the change tracker is free performance. Already applied to all `GetAllAsync` methods. Don't apply to `GetByIdAsync` (update paths re-attach the fetched entity).
- **Refetch with `.Include(...)` when a Create response needs a navigation property** — `AddAsync` + `SaveAsync` populates only the FK column, not the navigation reference. If the response DTO reads through a nav (e.g. `StockTransactionResponse(t)` → `t.BrokerAccount.Name`), the controller must re-read through a service method that applies `.Include(...)`. See `StockTransactionController.CreateAsync` for the pattern.
- **Prefer `AddHttpClient<TInterface, TImplementation>(...)` for typed clients consumed through an interface** — do **not** combine `AddHttpClient<TImpl>` with a wrapping `AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>())`: that pins a single `HttpClient` for the process lifetime, defeating `IHttpClientFactory`'s handler rotation (DNS refresh, socket recycling). The typed-client overload registers both interface and implementation correctly. See `NotionServiceRegister`.
- **Don't duplicate interface XML docs on the implementation** — in `Repository<T>` and similar, use `/// <inheritdoc />` on every implementing member rather than copying the interface `<summary>`. Keeps the contract in one place and prevents drift.
- **Hoist `.ToLower()` / `.ToLowerInvariant()` out of EF LINQ predicates** — compute the normalized comparand into a local before the `Where`/`FirstOrDefaultAsync`, so only the column-side call ends up in the translated expression tree. Use `ToLowerInvariant()` for the client-side side (project convention), e.g. `var lowered = brokerName.ToLowerInvariant(); … b.Name.ToLower() == lowered`. See `StockTransactionService.ResolveBrokerAsync`.
