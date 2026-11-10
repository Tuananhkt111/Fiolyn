# Auth Rules

Detailed rules for Google SSO + JWT auth in Fiolyn. Read this when touching anything under `Services/External/Google/`, `Services/Internal/{AuthService,JwtService,UserService,AllowedEmailService}`, `Controllers/{AuthController,UserController,AllowedEmailController}`, `Constants/AuthConstants.cs`, or `Extensions/ClaimsPrincipalExtensions.cs`.

> **What it does** (login flow, rotation, reuse detection, admin bootstrap): [docs/reference/auth.md](../reference/auth.md) ·
> **Telegram path:** [telegram-ingest.md](../reference/telegram-ingest.md) ·
> **Config keys:** [docs/guides/configuration.md](../guides/configuration.md)

## Rules

- **`Jwt:SigningKey` is mandatory, ≥32 chars.** Both `AuthServiceRegister` (startup) and `JwtService`'s constructor throw `InvalidOperationException` if missing or too short. No silent dev fallback.
- **Admin bootstrap is manual** — insert an `AllowedEmail`, let them log in once, then `UPDATE AppUser.IsAdmin = true`. No code path auto-promotes anyone.
- **Never hardcode `"Admin"` / `"isAdmin"` / `"true"`.** Use `AuthConstants.AdminPolicyName` / `AdminClaimName` / `AdminClaimTrueValue` from `Constants/AuthConstants.cs`.
- **`AppUser.NormalizeEmail(email)` is the canonical email normalizer** (lowercase + trim). Called by `AppUser.Email` and `AllowedEmail.Email` setters and by `AuthService`. Don't roll your own.
- **Read caller ID via `User.GetUserId()` / `User.TryGetUserId()`** (extensions in `Fiolyn.Extensions.ClaimsPrincipalExtensions`). Don't reach for `ClaimsPrincipal.FindFirst(...)` directly.
- **`AppUser` (entity) ≠ `User` (controller `ClaimsPrincipal`).** Inside a controller, `User` is the caller's identity — reference the entity as `AppUser` (or fully-qualified `Entities.AppUser`) to avoid the collision.
- **Raw refresh tokens leave `JwtService.CreateRefreshToken()` exactly once** — returned to the client, persisted as SHA-256. Reuse detection revokes all of the user's active tokens; `ReplacedByTokenHash` is recorded but not read.
- **A refresh re-checks the whitelist** and a logout checks token ownership — keep both when touching `AuthService` ([0023](../decisions/0023-sessions-follow-the-whitelist.md)).
- **`IssueTokensAsync` stages but does not save.** `AuthService.IssueTokensAsync` calls `AddAsync` on the new `RefreshToken` without `SaveAsync`; each caller (`LoginWithGoogleAsync`, `RefreshAsync`) flushes once at the end so user upsert + token insert (+ token revocation for refresh) land in one transaction.
- **`AppUser.TelegramId` is the Telegram whitelist** — there is no separate `TelegramUserInfo` table. The column is uniquely indexed; map the duplicate to 409 per [Race-safe uniqueness](ef-pitfalls.md#race-safe-uniqueness).
- **Resolve the Telegram sender through `AppUser.TelegramId` and pass that id on.** Ingest must call the same user-scoped service methods as the REST path (`CreateAsync(tx, userId)`), so membership checks apply identically. Never add an ingest-only bypass.