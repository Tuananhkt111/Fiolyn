# Plan: JWT Authentication (Access + Refresh Tokens)

## Context

Fiolyn is a .NET 8 Web API that will soon be consumed by a mobile app for managing stock investments. Today, `/api/stock-transactions` and `/api/broker-accounts` are publicly accessible — they must be locked down before the mobile frontend ships. The existing `TelegramUserInfo` table is a Telegram-ID allowlist, not a credential store, so a new auth layer is needed.

**Decisions:**
- Approach: **Custom User entity + JWT access tokens + refresh token rotation**.
- User scope: **Invite-only / admin-provisioned** — no public `/register` endpoint.
- Authorization: **Authenticated-vs-anonymous only** — no roles yet (can be added later without breaking changes).

**Outcome:** protected REST endpoints, short-lived access tokens, rotating refresh tokens for long mobile sessions, a way for the owner to provision new users, and unit + integration test coverage matching the project's existing test conventions.

---

## 1. Packages

Add to `Fiolyn/Fiolyn.csproj`:

- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.x — JWT validation middleware.
- `BCrypt.Net-Next` 4.0.x — password hashing.

`System.IdentityModel.Tokens.Jwt` is pulled in transitively by `JwtBearer`.

---

## 2. Entities

### `Fiolyn/Entities/User.cs` _(new)_

Implements `IAuditable` + `ISoftDelete` (project convention).

| Field           | Type                        | Notes                                           |
|-----------------|-----------------------------|-------------------------------------------------|
| `Id`            | `long`                      | PK, auto-increment                              |
| `Email`         | `string`                    | Required, unique, lowercased on set             |
| `PasswordHash`  | `string`                    | Required, BCrypt hash                           |
| `Created`       | `DateTime`                  | Auto-populated by `AppDbContext`                |
| `LastModified`  | `DateTime`                  | Auto-populated by `AppDbContext`                |
| `IsDeleted`     | `bool`                      | Auto-defaulted to false                         |
| `RefreshTokens` | `ICollection<RefreshToken>` | Navigation                                      |

Mirror the `StockTransaction.Symbol` normalization pattern (setter lowercases on assignment).

### `Fiolyn/Entities/RefreshToken.cs` _(new)_

Implements `IAuditable` (no soft-delete — revocation is explicit via `RevokedAt`).

| Field                  | Type        | Notes                                                        |
|------------------------|-------------|--------------------------------------------------------------|
| `Id`                   | `long`      | PK                                                           |
| `UserId`               | `long`      | FK → `User.Id`                                               |
| `User`                 | `User`      | Navigation                                                   |
| `TokenHash`            | `string`    | SHA-256 of raw token — raw token never persisted             |
| `ExpiresAt`            | `DateTime`  | UTC                                                          |
| `RevokedAt`            | `DateTime?` | Set when logout / rotated / reuse-detected                   |
| `ReplacedByTokenHash`  | `string?`   | Rotation chain — enables reuse-detection                     |
| `Created`              | `DateTime`  | Auto-populated                                               |
| `LastModified`         | `DateTime`  | Auto-populated                                               |

---

## 3. Data Layer

- `Fiolyn/Data/AppDbContext.cs` — add `DbSet<User> User` and `DbSet<RefreshToken> RefreshToken` (singular, per project convention). In `OnModelCreating`:
  - Unique index on `User.Email`.
  - Index on `RefreshToken.TokenHash`.
  - `RefreshToken.User` FK with `OnDelete(Cascade)`.
- Generate migration: `dotnet ef migrations add AddAuth --output-dir Data/Migrations`. Applied automatically on startup via the existing `Migrate()` call in `Program.cs`.

No new repository code — the generic `IRepository<T>` at `Fiolyn/Data/Repositories/Repository.cs` already covers the needed queries.

---

## 4. Settings

### `Fiolyn/ServiceRegisters/Settings/JwtSettings.cs` _(new)_

Mirror `Fiolyn/ServiceRegisters/Settings/NotionSettings.cs`.

| Field                       | Type   | Default   |
|-----------------------------|--------|-----------|
| `Issuer`                    | string | `"Fiolyn"` |
| `Audience`                  | string | `"FiolynMobile"` |
| `SigningKey`                | string | *(required, min 32 chars for HS256)* |
| `AccessTokenExpiryMinutes`  | int    | 15        |
| `RefreshTokenExpiryDays`    | int    | 30        |

### `Fiolyn/ServiceRegisters/Settings/InitialUserSettings.cs` _(new)_

Seeds the first account (since there's no public `/register`).

| Field      | Type   |
|------------|--------|
| `Email`    | string |
| `Password` | string |

Production values live in Northflank env vars:

```
JWT__SIGNINGKEY=...
JWT__ISSUER=Fiolyn
JWT__AUDIENCE=FiolynMobile
INITIALUSER__EMAIL=...
INITIALUSER__PASSWORD=...
```

Local values in `Fiolyn/appsettings.Development.json`. `appsettings.json` gets empty placeholders.

---

## 5. Service Registrar

### `Fiolyn/ServiceRegisters/AuthServiceRegister.cs` _(new, Order 1)_

- Binds `JwtSettings` and `InitialUserSettings` via `IOptions<T>`.
- Registers `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` with `TokenValidationParameters` from `JwtSettings` (validate issuer, audience, lifetime, signing key; zero clock skew).
- Registers `AddAuthorization()` with a default policy requiring an authenticated user.
- Registers `IAuthService` → `AuthService`, `ITokenService` → `TokenService`, `IPasswordHasher` → `BCryptPasswordHasher`.

Auto-discovered by the existing reflection-based loader. `UseAuthentication()` / `UseAuthorization()` are already wired in `Program.cs` — no pipeline change needed.

---

## 6. Services

### `Fiolyn/Services/Internal/ITokenService.cs` + `TokenService.cs` _(new)_

Pure token plumbing — no DB, no business logic.

- `string CreateAccessToken(User user)` — JWT with claims `sub`=UserId, `email`=Email, `jti`=Guid, `exp`/`iss`/`aud` from settings.
- `(string RawToken, string Hash) CreateRefreshToken()` — 64-byte cryptographically random token; returns both the raw string (for client) and its SHA-256 hash (for DB lookup).
- `string Hash(string rawToken)` — shared hashing helper.

### `Fiolyn/Services/Internal/IPasswordHasher.cs` + `BCryptPasswordHasher.cs` _(new)_

Thin wrapper over BCrypt so `AuthService` tests can substitute it without pulling BCrypt into the test project.

- `string Hash(string password)`
- `bool Verify(string password, string hash)`

### `Fiolyn/Services/Internal/IAuthService.cs` + `AuthService.cs` _(new)_

Dependencies: `IRepository<User>`, `IRepository<RefreshToken>`, `ITokenService`, `IPasswordHasher`, `IOptions<JwtSettings>`.

- `Task<AuthResponse> LoginAsync(string email, string password)` — lookup via `QueryActive()` + case-insensitive email match; verify password; throw `UnauthorizedAccessException` on any failure (single error message — never reveal which check failed); issue access + refresh token pair; persist refresh token hash.
- `Task<AuthResponse> RefreshAsync(string rawRefreshToken)` — hash input; find matching token; if **revoked** token is presented → revoke the entire chain for that user (reuse-detection) and throw; if expired → throw; otherwise mark current token revoked with `ReplacedByTokenHash` and issue new pair.
- `Task LogoutAsync(string rawRefreshToken)` — mark the token revoked.
- `Task<User> CreateUserAsync(string email, string password)` — validates email is not already taken via `QueryActive()`; BCrypt-hashes; persists. Used by `POST /api/auth/users` and by the startup seeder.

Throw `UnauthorizedAccessException` / `InvalidOperationException` on failure so `GlobalExceptionHandler` produces clean ProblemDetails responses.

---

## 7. Controllers & DTOs

### `Fiolyn/Controllers/AuthController.cs` _(new, `[Route("api/auth")]`)_

| Endpoint                  | Auth             | Returns               |
|---------------------------|------------------|-----------------------|
| `POST /api/auth/login`    | `[AllowAnonymous]` | 200 `AuthResponse`  |
| `POST /api/auth/refresh`  | `[AllowAnonymous]` | 200 `AuthResponse`  |
| `POST /api/auth/logout`   | `[Authorize]`      | 204                 |
| `POST /api/auth/users`    | `[Authorize]`      | 201 `UserResponse`  |

### `Fiolyn/Controllers/DTOs/Requests/` _(new)_

- `LoginRequest` — `Email`, `Password` (`[Required]`).
- `RefreshTokenRequest` — `RefreshToken` (`[Required]`).
- `CreateUserRequest` — `Email` (`[Required, EmailAddress]`), `Password` (`[Required, MinLength(8)]`).

### `Fiolyn/Controllers/DTOs/Responses/` _(new)_

- `AuthResponse` — `AccessToken`, `RefreshToken`, `ExpiresAt` (UTC).
- `UserResponse` — `Id`, `Email`, `Created`.

### Protect existing controllers

- `Fiolyn/Controllers/StockTransactionController.cs` — class-level `[Authorize]`.
- `Fiolyn/Controllers/BrokerAccountController.cs` — class-level `[Authorize]`.
- `Fiolyn/Controllers/TelegramController.cs` — class-level `[AllowAnonymous]` (webhook must stay public and always return 200).

---

## 8. Startup Seeding

### `Fiolyn/Infrastructure/InitialUserSeeder.cs` _(new)_

A small helper invoked from `Program.cs` after `db.Database.Migrate()`:

```
if no User exists and InitialUserSettings has Email + Password:
    await authService.CreateUserAsync(email, password)
```

Idempotent — on subsequent startups at least one user exists, so the seeder returns immediately.

---

## 9. Global Exception Handler

`Fiolyn/Infrastructure/GlobalExceptionHandler.cs` already handles `KeyNotFoundException` → 404. Add one mapping:

- `UnauthorizedAccessException` → 401 with a generic `"Invalid credentials."` detail (never leak which check failed).

---

## 10. Testing

Follow conventions in `Fiolyn.Tests/`.

**Unit tests** (`Fiolyn.Tests/Unit/`):

- `Services/TokenServiceTests.cs` — access token contains expected claims, expiry computed from settings, refresh raw/hash pair is consistent.
- `Services/AuthServiceTests.cs` — login success; login with unknown email throws; login with wrong password throws; refresh rotates correctly; revoked-token replay triggers chain revocation; logout revokes; `CreateUserAsync` rejects duplicate email. Mock `IRepository<User>` / `IRepository<RefreshToken>` with NSubstitute + `BuildMock()` (same pattern as `StockTransactionServiceTests.cs`).
- `Controllers/AuthControllerTests.cs` — endpoint status codes.

**Integration tests** (`Fiolyn.Tests/Integration/`):

- *(Optional)* `Data/AuthMigrationTests.cs` — boots in-memory context, verifies `User` + `RefreshToken` sets are wired.

All existing tests must still pass (`dotnet test` — currently 44 tests per CLAUDE.md).

---

## 11. Files to Create / Modify

**Create:**

- `Fiolyn/Entities/User.cs`, `Fiolyn/Entities/RefreshToken.cs`
- `Fiolyn/ServiceRegisters/Settings/JwtSettings.cs`, `InitialUserSettings.cs`
- `Fiolyn/ServiceRegisters/AuthServiceRegister.cs`
- `Fiolyn/Services/Internal/ITokenService.cs`, `TokenService.cs`
- `Fiolyn/Services/Internal/IAuthService.cs`, `AuthService.cs`
- `Fiolyn/Services/Internal/IPasswordHasher.cs`, `BCryptPasswordHasher.cs`
- `Fiolyn/Controllers/AuthController.cs`
- `Fiolyn/Controllers/DTOs/Requests/LoginRequest.cs`, `RefreshTokenRequest.cs`, `CreateUserRequest.cs`
- `Fiolyn/Controllers/DTOs/Responses/AuthResponse.cs`, `UserResponse.cs`
- `Fiolyn/Infrastructure/InitialUserSeeder.cs`
- `Fiolyn/Data/Migrations/<timestamp>_AddAuth.cs` _(auto-generated)_
- Test files under `Fiolyn.Tests/Unit/Services/` and `Fiolyn.Tests/Unit/Controllers/`

**Modify:**

- `Fiolyn/Fiolyn.csproj` — add two packages.
- `Fiolyn/Data/AppDbContext.cs` — add DbSets + `OnModelCreating` index config.
- `Fiolyn/Program.cs` — invoke `InitialUserSeeder` after `Migrate()`.
- `Fiolyn/Controllers/StockTransactionController.cs` — add `[Authorize]`.
- `Fiolyn/Controllers/BrokerAccountController.cs` — add `[Authorize]`.
- `Fiolyn/Controllers/TelegramController.cs` — add `[AllowAnonymous]`.
- `Fiolyn/Infrastructure/GlobalExceptionHandler.cs` — map `UnauthorizedAccessException` → 401.
- `Fiolyn/appsettings.json` — add empty `Jwt` + `InitialUser` sections (no secrets committed).
- `CLAUDE.md` — add a short Authentication subsection under Architecture.

---

## 12. Verification

1. `mcp__ide__getDiagnostics` shows no compile errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — all existing tests + new auth tests pass.
3. `dotnet ef migrations add AddAuth --output-dir Data/Migrations` succeeds; `dotnet ef database update` applies cleanly against a local Neon branch.
4. Manual smoke test via Swagger / curl:
   - Start app → seeder creates initial user from config.
   - `POST /api/auth/login` with correct creds → 200 + token pair.
   - `GET /api/stock-transactions` without `Authorization` header → 401.
   - `GET /api/stock-transactions` with `Authorization: Bearer <access>` → 200.
   - `POST /api/auth/refresh` with refresh token → 200 + new pair; replaying the old refresh token → 401 and all descendant tokens revoked.
   - `POST /api/auth/logout` → 204; subsequent refresh with the same token → 401.
   - `POST /api/telegram` still accepts unauthenticated requests (webhook unchanged).