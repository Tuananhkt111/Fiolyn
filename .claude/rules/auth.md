# Auth Rules

Detailed rules for Google SSO + JWT auth in Fiolyn. Read this when touching anything under `Services/External/Google/`, `Services/Internal/{AuthService,JwtService,UserService,AllowedEmailService}`, `Controllers/{AuthController,UserController,AllowedEmailController}`, `Constants/AuthConstants.cs`, or `Extensions/ClaimsPrincipalExtensions.cs`.

## Flow

Web client gets a Google ID token via Google Identity Services (GIS) → `POST /api/auth/google` → `GoogleTokenVerifier` validates against `GoogleAuth:ClientId` → `AllowedEmail` whitelist check → `AppUser` upsert (merged lookup by `GoogleSub` OR `Email`) → `JwtService` issues HS256 access token + opaque refresh token (64 random bytes, returned raw once, persisted as SHA-256 hash). `POST /api/auth/refresh` rotates the refresh token atomically (new insert + old revoke in one `SaveChangesAsync`); replaying a revoked token revokes the whole rotation chain for that user. `POST /api/auth/logout` sets `RevokedAt`.

Authorization uses class-level `[Authorize]` plus a named `Admin` policy (requires `isAdmin=true` claim).

Telegram ingest is a separate authentication path: `POST /api/telegram` → `TelegramMessageService` looks up the sender's Telegram user ID against `AppUser.TelegramId` (only active, non-soft-deleted users). No match → skip; match → proceed as that `AppUser`. Stock writes inherit the broker-membership check via `CreateAsync(tx, userId)`.

## Rules

- **`Jwt:SigningKey` is mandatory, ≥32 chars.** Both `AuthServiceRegister` (startup) and `JwtService`'s constructor throw `InvalidOperationException` if missing or too short. No silent dev fallback.
- **Admin bootstrap is manual** — insert an `AllowedEmail`, let them log in once, then `UPDATE AppUser.IsAdmin = true`. No code path auto-promotes anyone.
- **Never hardcode `"Admin"` / `"isAdmin"` / `"true"`.** Use `AuthConstants.AdminPolicyName` / `AdminClaimName` / `AdminClaimTrueValue` from `Constants/AuthConstants.cs`.
- **`AppUser.NormalizeEmail(email)` is the canonical email normalizer** (lowercase + trim). Called by `AppUser.Email` and `AllowedEmail.Email` setters and by `AuthService`. Don't roll your own.
- **Read caller ID via `User.GetUserId()` / `User.TryGetUserId()`** (extensions in `Fiolyn.Extensions.ClaimsPrincipalExtensions`). Don't reach for `ClaimsPrincipal.FindFirst(...)` directly.
- **`AppUser` (entity) ≠ `User` (controller `ClaimsPrincipal`).** Inside a controller, `User` is the caller's identity — reference the entity as `AppUser` (or fully-qualified `Entities.AppUser`) to avoid the collision.
- **Raw refresh tokens leave `JwtService.CreateRefreshToken()` exactly once** — returned to the client, persisted as SHA-256. Reuse detection relies on the `ReplacedByTokenHash` rotation chain.
- **`IssueTokensAsync` stages but does not save.** `AuthService.IssueTokensAsync` calls `AddAsync` on the new `RefreshToken` without `SaveAsync`; each caller (`LoginWithGoogleAsync`, `RefreshAsync`) flushes once at the end so user upsert + token insert (+ token revocation for refresh) land in one transaction.
- **`AppUser.TelegramId` is the Telegram whitelist.** Linked via `PUT /api/users/me/telegram-id` (self) or `PUT /api/users/{id}/telegram-id` (admin policy), cleared via the matching `DELETE`. The column has a unique index; a duplicate attempt surfaces as 409 `Conflict` via `ConflictException` from `UserService.UpdateTelegramIdAsync`. There is no separate `TelegramUserInfo` table.

## Tests

`AuthServiceTests`, `JwtServiceTests`, `GoogleTokenVerifierTests`, `AllowedEmailServiceTests`, `UserServiceTests` in `Fiolyn.Tests/Unit/Services/`. `AuthControllerTests`, `UserControllerTests`, `AllowedEmailControllerTests` in `Fiolyn.Tests/Unit/Controllers/`.