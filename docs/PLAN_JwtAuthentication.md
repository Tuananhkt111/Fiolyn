# Plan: Google SSO + JWT Authentication (Admin-Managed Whitelist)

## Context

Fiolyn is a .NET 8 Web API that will soon be consumed by a mobile stock-management app. Today `/api/stock-transactions` and `/api/broker-accounts` are publicly accessible, and the only "user" concept is `TelegramUserInfo` (a Telegram-ID allowlist, unrelated to API callers).

Before the mobile frontend ships, the API needs:
- **Google SSO** login (no password storage — outsource identity to Google).
- **Admin-managed allow-list** of Google emails permitted to sign in.
- A **User Info** record per signed-in user (for audit, future ownership of transactions, profile display on mobile).
- **JWT issued by Fiolyn itself** so the mobile client authenticates to Fiolyn with a Fiolyn-signed token (not a Google token) — we control revocation, rotation, and authorization.

Middleware is already half-wired: `Program.cs` calls `UseAuthentication()`/`UseAuthorization()` (lines 19–20) but no scheme is registered. `GlobalExceptionHandler` doesn't yet map 401/403. No auth packages are in `Fiolyn.csproj`.

## Decisions (confirmed)

| Axis | Choice |
|------|--------|
| SSO flow | **Mobile Google SDK → backend verifies ID token** via `POST /api/auth/google { idToken }`. No client secret needed. (Same endpoint also works for OAuth code+PKCE clients.) |
| Token lifecycle | **Access + refresh with rotation + reuse detection.** 15-min access JWT; 30-day refresh token stored hashed in DB; each refresh rotates the token; replaying a revoked token revokes the whole chain. |
| Authorization model | **Admin + User roles.** `IsAdmin` bool on `User`; admin policy for whitelist/user-management endpoints. |
| User provisioning | **Auto-create on first login** if email is in `AllowedEmail`. First-time login for `InitialAdmin.Email` sets `IsAdmin = true`. |

## Login flow (happy path)

```
[Mobile]                                      [Fiolyn API]                [Google]
  1. Tap "Sign in with Google" (native SDK) --------------------------------> verify
  2. Google returns ID token (JWT signed by Google) <------------------------
  3. POST /api/auth/google { idToken } ---->
                                              4. GoogleJsonWebSignature
                                                 .ValidateAsync(idToken)
                                              5. lookup AllowedEmail
                                                 → 403 if not listed
                                              6. upsert User (by GoogleSub
                                                 or email); set IsAdmin=true
                                                 if email == InitialAdmin
                                              7. issue Fiolyn JWT + refresh
                                                 (persist refresh hash)
  8. { accessToken, refreshToken, expiresAt } <---
  9. GET /api/stock-transactions
     Authorization: Bearer <fiolynAccessToken> ---->
                                              JwtBearer middleware validates
                                              Fiolyn's own JWT
  10. 200 OK <---
 (access token expires)
 11. POST /api/auth/refresh { refreshToken } ---->
                                              find by hash; if revoked ->
                                              revoke chain + 401; else rotate
 12. new { accessToken, refreshToken } <---
```

---

## 1. Packages (add to `Fiolyn/Fiolyn.csproj`)

- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.x — validates Fiolyn JWTs on each request.
- `Google.Apis.Auth` 1.68.x — `GoogleJsonWebSignature.ValidateAsync` verifies Google ID tokens.

## 2. Entities (new, `Fiolyn/Entities/`)

All three follow the project conventions (primary constructors not applicable to entities; existing entities like `TelegramUserInfo`, `BrokerAccount`, `StockTransaction` are the shape template).

**`User.cs`** — implements `IAuditable` + `ISoftDelete`.
- `long Id` (PK)
- `string Email` — unique; setter lowercases on assignment (mirror `StockTransaction.Symbol` → `NormalizeSymbol` pattern at `Fiolyn/Entities/StockTransaction.cs`).
- `string GoogleSub` — unique; stable Google account ID from ID token `sub` claim.
- `string? DisplayName`, `string? PictureUrl`
- `bool IsAdmin`
- `DateTime? LastLoginAt`
- `Created`, `LastModified`, `IsDeleted`
- Nav: `ICollection<RefreshToken>`

**`AllowedEmail.cs`** — implements `IAuditable` + `ISoftDelete`.
- `long Id` (PK)
- `string Email` — unique; lowercased in setter.
- `string? Note` (e.g. "my brother")
- `long? InvitedByUserId` (FK → `User.Id`, nullable — null for config-seeded entries)
- audit + soft-delete fields

**`RefreshToken.cs`** — implements `IAuditable` only (revocation is via `RevokedAt`, not soft-delete).
- `long Id`, `long UserId` (FK, cascade)
- `string TokenHash` — SHA-256 of raw token (raw never persisted)
- `DateTime ExpiresAt` (UTC)
- `DateTime? RevokedAt`
- `string? ReplacedByTokenHash` — rotation chain, enables reuse detection
- `Created`, `LastModified`

## 3. Data layer

- `Fiolyn/Data/AppDbContext.cs` — add `DbSet<User> User`, `DbSet<AllowedEmail> AllowedEmail`, `DbSet<RefreshToken> RefreshToken` (singular names per CLAUDE.md). **No `OnModelCreating` override needed** — EF Core conventions handle everything once we use data-annotation attributes:
  - On `User`: `[Index(nameof(Email), IsUnique = true)]`, `[Index(nameof(GoogleSub), IsUnique = true)]` at class level.
  - On `AllowedEmail`: `[Index(nameof(Email), IsUnique = true)]`.
  - On `RefreshToken`: `[Index(nameof(TokenHash))]` (non-unique lookup index).
  - FK + cascade: because `RefreshToken.UserId` is non-nullable `long` with nav `User` and inverse `User.RefreshTokens`, EF convention infers a required one-to-many with `OnDelete(Cascade)` automatically. No fluent config needed.
- Generate migration: `dotnet ef migrations add AddAuth --output-dir Data/Migrations`.
- **Migration application (new runtime behavior):** today `Program.cs` has no `Migrate()` call, so migrations are applied manually via CLI. Introduce an async init step (see §8) to apply migrations on startup. This is a behavior change worth noting but aligns with CLAUDE.md's stated convention ("applied on startup via `Migrate()`").
- **No new repositories** — generic `IRepository<T>` at `Fiolyn/Data/Repositories/Repository.cs` covers all needed queries.

## 4. Settings (new, `Fiolyn/ServiceRegisters/Settings/` — mirror `NotionSettings.cs`)

- **`JwtSettings`** — `Issuer` ("Fiolyn"), `Audience` ("FiolynMobile"), `SigningKey` (required, ≥32 chars HS256), `AccessTokenExpiryMinutes` (15), `RefreshTokenExpiryDays` (30).
- **`GoogleAuthSettings`** — `ClientId` (Google OAuth client ID; used as the expected `aud` when verifying Google ID tokens). **No client secret needed** for Flow 1A.
- **`InitialAdminSettings`** — `Email` (one address; promoted to admin on first login if its email is whitelisted).

Production env vars (Northflank):
```
JWT__SIGNINGKEY=<random ≥32 chars>
JWT__ISSUER=Fiolyn
JWT__AUDIENCE=FiolynMobile
GOOGLEAUTH__CLIENTID=...apps.googleusercontent.com
INITIALADMIN__EMAIL=you@example.com
```
Dev values in `Fiolyn/appsettings.Development.json`; empty placeholders in `Fiolyn/appsettings.json`.

## 5. Service registration

**Split responsibilities across existing registrars (keep concerns separate):**

- **New** `Fiolyn/ServiceRegisters/AuthServiceRegister.cs` (Order 1) — **only auth scheme + policies + settings binding**:
  - Bind `JwtSettings`, `GoogleAuthSettings`, `InitialAdminSettings` via `IOptions<T>`.
  - `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` with `TokenValidationParameters` from `JwtSettings` (validate issuer, audience, lifetime, signing key; `ClockSkew = TimeSpan.Zero`).
  - `AddAuthorization(opts => opts.AddPolicy("Admin", p => p.RequireClaim("isAdmin", "true")))` — default policy = authenticated.

- **Modify** `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs` — add the five new scoped business services alongside existing ones:
  ```csharp
  services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>() ;
  services.AddScoped<IJwtService, JwtService>() ;
  services.AddScoped<IAuthService, AuthService>() ;
  services.AddScoped<IAllowedEmailService, AllowedEmailService>() ;
  services.AddScoped<IUserService, UserService>() ;
  ```

Both registrars are auto-discovered by the existing reflection-based loader (`ServiceRegistrarExtensions.RegisterAllServices`).

## 6. Services (new)

- **`Services/External/Google/IGoogleTokenVerifier.cs` + `GoogleTokenVerifier.cs`** — wraps `GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings{ Audience = [settings.ClientId] })`. Returns `GooglePayload(Email, Sub, Name, Picture)` on success, throws `UnauthorizedAccessException` on invalid token. Mockable for tests.
- **`Services/Internal/IJwtService.cs` + `JwtService.cs`** — pure plumbing (no DB):
  - `string CreateAccessToken(User user)` — JWT with `sub=Id`, `email`, `name`, `isAdmin` ("true"/"false"), `jti=Guid`, issuer/audience/expiry from settings.
  - `(string RawToken, string Hash) CreateRefreshToken()` — 64-byte cryptographically random; returns raw + SHA-256 hash.
  - `string Hash(string raw)` — shared.
- **`Services/Internal/IAuthService.cs` + `AuthService.cs`** — deps: `IGoogleTokenVerifier`, `IJwtService`, `IRepository<User>`, `IRepository<AllowedEmail>`, `IRepository<RefreshToken>`, `IOptions<JwtSettings>`, `IOptions<InitialAdminSettings>`.
  - `Task<AuthResponse> LoginWithGoogleAsync(string idToken)` — verify ID token → check `AllowedEmail.QueryActive()` contains email (else throw `ForbiddenAccessException`) → upsert `User` by `GoogleSub` (fallback `Email`); set `IsAdmin = true` if email matches `InitialAdmin.Email` and the User has no admin yet; update `LastLoginAt` → issue token pair → persist refresh token hash → return.
  - `Task<AuthResponse> RefreshAsync(string rawRefreshToken)` — hash, lookup; if not found / expired → throw `UnauthorizedAccessException`; if `RevokedAt != null` → **reuse detected**: revoke all tokens in the same chain for that user, throw; else rotate (set `RevokedAt`, `ReplacedByTokenHash`) and issue new pair.
  - `Task LogoutAsync(string rawRefreshToken)` — set `RevokedAt`.
- **`Services/Internal/IAllowedEmailService.cs` + `AllowedEmailService.cs`** — admin CRUD over `AllowedEmail`.
- **`Services/Internal/IUserService.cs` + `UserService.cs`** — `GetMeAsync(userId)`, `UpdateMeAsync(userId, dto)` (display name only); admin-only: `ListAsync()`, `SetAdminAsync(id, isAdmin)`, `SoftDeleteAsync(id)`.

Register all services in `ScopedBusinessServicesRegister.cs` (matches existing pattern).

## 7. Controllers & DTOs

All request/response DTOs are `record` types under `Controllers/DTOs/Requests/` and `/Responses/` (per project convention; use `[Required]`/`[EmailAddress]` on requests).

| Endpoint | Method | Auth | Body / Returns |
|---|---|---|---|
| `/api/auth/google` | POST | `[AllowAnonymous]` | `GoogleLoginRequest{IdToken}` → 200 `AuthResponse` |
| `/api/auth/refresh` | POST | `[AllowAnonymous]` | `RefreshTokenRequest{RefreshToken}` → 200 `AuthResponse` |
| `/api/auth/logout` | POST | `[Authorize]` | `RefreshTokenRequest` → 204 |
| `/api/users/me` | GET | `[Authorize]` | → 200 `UserResponse` |
| `/api/users/me` | PATCH | `[Authorize]` | `UpdateMeRequest{DisplayName?}` → 200 `UserResponse` |
| `/api/users` | GET | `[Authorize(Policy="Admin")]` | → 200 `UserResponse[]` |
| `/api/users/{id}` | PATCH | `[Authorize(Policy="Admin")]` | `UpdateUserRequest{IsAdmin?}` → 200 |
| `/api/users/{id}` | DELETE | `[Authorize(Policy="Admin")]` | → 204 (soft delete) |
| `/api/allowed-emails` | GET | `[Authorize(Policy="Admin")]` | → 200 `AllowedEmailResponse[]` |
| `/api/allowed-emails` | POST | `[Authorize(Policy="Admin")]` | `AllowedEmailRequest{Email,Note?}` → 201 |
| `/api/allowed-emails/{id}` | DELETE | `[Authorize(Policy="Admin")]` | → 204 (soft delete) |

Responses: `AuthResponse(AccessToken, RefreshToken, ExpiresAt)`, `UserResponse(Id, Email, DisplayName, PictureUrl, IsAdmin, LastLoginAt, Created)`, `AllowedEmailResponse(Id, Email, Note, InvitedByUserId, Created)`.

**Protect existing controllers:**
- `Controllers/StockTransactionController.cs`, `Controllers/BrokerAccountController.cs` — class-level `[Authorize]`.
- `Controllers/TelegramController.cs` — class-level `[AllowAnonymous]` (webhook must stay public and always return 200, per CLAUDE.md key rules).

## 8. Startup bootstrap — migrations + seeder (co-located with `DatabaseServiceRegister`)

Keep all DB lifecycle wiring in one place. Create the seeder as a thin helper and expose a single startup extension on `DatabaseServiceRegister` that `Program.cs` calls after `Build()`.

**New: `Fiolyn/Infrastructure/InitialAdminSeeder.cs`**
- Dependencies: `AppDbContext`, `IOptions<InitialAdminSettings>`.
- `async Task SeedAsync()`:
  - If `InitialAdmin.Email` is blank → return.
  - If `AllowedEmail.QueryActive()` already contains that email → return (idempotent).
  - Otherwise insert an `AllowedEmail` row (lowercased) with `InvitedByUserId = null`, `Note = "InitialAdmin (seeded)"`.
- Do **not** pre-create the `User`. `AuthService.LoginWithGoogleAsync` will upsert the User on first Google sign-in and set `IsAdmin = true` because the email matches `InitialAdmin.Email`.

**Modify `Fiolyn/ServiceRegisters/DatabaseServiceRegister.cs`:**
- Register the seeder: `services.AddScoped<InitialAdminSeeder>()`.
- Add a static extension method `InitializeDatabaseAsync(this WebApplication app)` in the same file (or a sibling static class `DatabaseStartupExtensions`) that:
  ```csharp
  using var scope = app.Services.CreateScope();
  var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
  await db.Database.MigrateAsync();
  var seeder = scope.ServiceProvider.GetRequiredService<InitialAdminSeeder>();
  await seeder.SeedAsync();
  ```

**Modify `Fiolyn/Program.cs`** — one line before `app.Run()`:
```csharp
await app.InitializeDatabaseAsync();
```

This solves the admin-bootstrap with no password, and co-locates migration + seed concerns with the DB registrar.

## 9. Global exception handler

`Fiolyn/Infrastructure/GlobalExceptionHandler.cs` — add two mappings to the existing `switch`:
- `UnauthorizedAccessException` → 401, title `"Unauthorized"`, detail `"Unauthorized"` (never leak which check failed).
- `ForbiddenAccessException` (new class in `Fiolyn/Infrastructure/`) → 403, title `"Forbidden"`.

## 10. Tests (`Fiolyn.Tests/`)

Follow existing conventions: xUnit + NSubstitute + FluentAssertions + `MockQueryable.BuildMock()` for `QueryActive()`.

Unit (`Unit/Services/`):
- `JwtServiceTests` — access token contains expected claims; expiry from settings; refresh raw/hash pair consistency.
- `GoogleTokenVerifierTests` — happy path vs invalid token (substitute the `ValidationSettings` call).
- `AuthServiceTests` — login with whitelisted email upserts User; email not whitelisted → `ForbiddenAccessException`; first login with `InitialAdmin.Email` sets `IsAdmin=true`; refresh rotates + returns new pair; replaying a revoked token revokes the whole chain; logout revokes.
- `AllowedEmailServiceTests`, `UserServiceTests` — CRUD happy paths + guard conditions.

Unit (`Unit/Controllers/`):
- `AuthControllerTests` — endpoint status codes.

All 44 existing tests must still pass (`dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj`).

## 11. Files to create / modify

**Create:**
- `Fiolyn/Entities/User.cs`, `AllowedEmail.cs`, `RefreshToken.cs`
- `Fiolyn/ServiceRegisters/Settings/JwtSettings.cs`, `GoogleAuthSettings.cs`, `InitialAdminSettings.cs`
- `Fiolyn/ServiceRegisters/AuthServiceRegister.cs`
- `Fiolyn/Services/External/Google/IGoogleTokenVerifier.cs`, `GoogleTokenVerifier.cs`
- `Fiolyn/Services/Internal/IJwtService.cs`, `JwtService.cs`
- `Fiolyn/Services/Internal/IAuthService.cs`, `AuthService.cs`
- `Fiolyn/Services/Internal/IAllowedEmailService.cs`, `AllowedEmailService.cs`
- `Fiolyn/Services/Internal/IUserService.cs`, `UserService.cs`
- `Fiolyn/Controllers/AuthController.cs`, `UserController.cs`, `AllowedEmailController.cs`
- `Fiolyn/Controllers/DTOs/Requests/GoogleLoginRequest.cs`, `RefreshTokenRequest.cs`, `AllowedEmailRequest.cs`, `UpdateUserRequest.cs`, `UpdateMeRequest.cs`
- `Fiolyn/Controllers/DTOs/Responses/AuthResponse.cs`, `UserResponse.cs`, `AllowedEmailResponse.cs`
- `Fiolyn/Infrastructure/InitialAdminSeeder.cs`, `ForbiddenAccessException.cs`
- `Fiolyn/Data/Migrations/<timestamp>_AddAuth.cs` *(auto-generated)*
- Test files under `Fiolyn.Tests/Unit/Services/` and `Fiolyn.Tests/Unit/Controllers/`

**Modify:**
- `Fiolyn/Fiolyn.csproj` — add the two packages.
- `Fiolyn/Data/AppDbContext.cs` — add three DbSets (no `OnModelCreating` changes needed; indexes live on the entities via `[Index]` attributes).
- `Fiolyn/Program.cs` — add `await app.InitializeDatabaseAsync()` before `app.Run()`.
- `Fiolyn/Controllers/StockTransactionController.cs`, `BrokerAccountController.cs` — class-level `[Authorize]`.
- `Fiolyn/Controllers/TelegramController.cs` — class-level `[AllowAnonymous]`.
- `Fiolyn/Infrastructure/GlobalExceptionHandler.cs` — add 401 + 403 mappings.
- `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs` — register `IGoogleTokenVerifier`, `IJwtService`, `IAuthService`, `IAllowedEmailService`, `IUserService`.
- `Fiolyn/ServiceRegisters/DatabaseServiceRegister.cs` — register `InitialAdminSeeder` and expose `InitializeDatabaseAsync` extension (runs migrations + seeder).
- `Fiolyn/appsettings.json` — add empty `Jwt`, `GoogleAuth`, `InitialAdmin` sections.
- `CLAUDE.md` — add a short Authentication subsection under Architecture.

## 12. Verification

1. `mcp__ide__getDiagnostics` → no compile errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — existing 44 + new auth tests pass.
3. `dotnet ef migrations add AddAuth --output-dir Data/Migrations` succeeds; `dotnet ef database update` applies cleanly against a local Neon branch.
4. Manual smoke test (Postman / mobile):
   - App starts → seeder ensures admin email is in `AllowedEmail`.
   - `POST /api/auth/google` with a real Google ID token for the admin email → 200 + tokens; `User` row created with `IsAdmin=true`.
   - `GET /api/stock-transactions` without `Authorization` → 401.
   - `GET /api/stock-transactions` with `Authorization: Bearer <access>` → 200.
   - `POST /api/auth/google` with ID token for a non-whitelisted email → 403.
   - `POST /api/allowed-emails` as admin → 201; as non-admin → 403.
   - `POST /api/auth/refresh` → 200 + new pair; replaying the old refresh token → 401 + all descendant tokens revoked in DB.
   - `POST /api/auth/logout` → 204; subsequent refresh with the same token → 401.
   - `POST /api/telegram` still accepts unauthenticated requests.
