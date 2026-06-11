# Authentication & User Management

Describes the web auth subsystem: Google SSO login, Fiolyn-signed JWTs, refresh-token
rotation, the email whitelist that gates account creation, and admin user management.

> **Canonical rules:** `.claude/rules/auth.md` (flow + invariants) and
> `.claude/rules/access-control.md` (ownership, admin endpoint split). This file is the
> feature-level overview; those rules are authoritative when implementing.

## Identity model

- **`AppUser`** — created on first successful Google login. Carries `Email` (normalized via
  `AppUser.NormalizeEmail`), `GoogleSub`, `DisplayName`, `PictureUrl`, `IsAdmin`,
  `LastLoginAt`, and the optional `TelegramId`. Implements `IAuditable` + `ISoftDelete`.
- **`AllowedEmail`** — admin-managed whitelist. A Google login is rejected unless the
  account's email is present here.
- **`RefreshToken`** — opaque token persisted as a SHA-256 hash. The only entity that uses
  explicit revocation (`RevokedAt`) instead of soft delete. Rotation chain tracked via
  `ReplacedByTokenHash`; `RevokedAt` carries `[ConcurrencyCheck]`.

`AppUser.TelegramId` doubles as the Telegram-ingest whitelist — see the Telegram ingest path
in `.claude/rules/auth.md` and `docs/LocalWebhookTesting.md`.

## Login flow

1. Web client obtains a Google ID token via Google Identity Services (GIS).
2. `POST /api/auth/google` → `GoogleTokenVerifier` validates the token against
   `GoogleAuth:ClientId`.
3. Email is checked against the `AllowedEmail` whitelist (not whitelisted → rejected).
4. `AppUser` upsert — merged lookup by `GoogleSub` **or** `Email`.
5. `JwtService` issues an HS256 access token (signed with `Jwt:SigningKey`, ≥32 chars,
   mandatory) plus an opaque refresh token (64 random bytes, returned raw exactly once,
   stored as SHA-256).

The user upsert + refresh-token insert land in a **single** `SaveChangesAsync`
(`IssueTokensAsync` stages but does not save; each caller flushes once).

## Token rotation & logout

- `POST /api/auth/refresh` — rotates atomically: new token inserted + old token revoked in one
  save. Replaying an already-revoked token revokes the entire rotation chain for that user
  (reuse detection). Concurrent refreshes on the same token race on the `[ConcurrencyCheck]`;
  the loser surfaces `401`.
- `POST /api/auth/logout` — sets `RevokedAt` on the supplied refresh token. Requires a valid
  access token.

## Authorization

- Class-level `[Authorize]` on every protected controller.
- A named `Admin` policy requires the `isAdmin=true` claim. Reference it via
  `AuthConstants.AdminPolicyName` / `AdminClaimName` / `AdminClaimTrueValue` — never hardcode
  the strings.
- Read the caller's id with `User.GetUserId()` / `User.TryGetUserId()` (extensions in
  `ClaimsPrincipalExtensions`), never `FindFirst(...)` directly.

## Endpoints

### Auth — `AuthController` (`/api/auth`)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/google` | anonymous | Exchange a Google ID token for access + refresh tokens |
| POST | `/api/auth/refresh` | anonymous | Rotate a refresh token |
| POST | `/api/auth/logout` | authenticated | Revoke a refresh token |

### Profile & user management — `UserController` (`/api/users`)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/users/me` | self | Current user's profile |
| PATCH | `/api/users/me` | self | Update own display name |
| PUT | `/api/users/me/telegram-id` | self | Link/update own Telegram ID (409 if taken) |
| DELETE | `/api/users/me/telegram-id` | self | Unlink own Telegram ID |
| GET | `/api/users` | admin | List all active users (paginated) |
| PATCH | `/api/users/{id}` | admin | Toggle a user's admin flag |
| PUT | `/api/users/{id}/telegram-id` | admin | Set/update a user's Telegram ID (409 if taken) |
| DELETE | `/api/users/{id}/telegram-id` | admin | Clear a user's Telegram ID |
| DELETE | `/api/users/{id}` | admin | Soft-delete a user |

### Email whitelist — `AllowedEmailController` (`/api/allowed-emails`, admin only)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/allowed-emails` | List active whitelisted emails (paginated) |
| POST | `/api/allowed-emails` | Add an email (records inviting user) |
| DELETE | `/api/allowed-emails/{id}` | Soft-delete a whitelist entry |

`TelegramId` has a unique index; a duplicate link attempt surfaces as `409 Conflict`
(`ConflictException`). Whitelist email creation maps Postgres `23505` to `409` likewise.

## Admin bootstrap (manual)

There is no auto-promotion code path:

1. Insert an `AllowedEmail` row for the target address.
2. Let that account log in once (creates the `AppUser`).
3. `UPDATE AppUser SET IsAdmin = true` for that row.

Thereafter that user can manage the whitelist and toggle other users' admin flags via the
endpoints above.

## Tests

`AuthServiceTests`, `JwtServiceTests`, `GoogleTokenVerifierTests`, `AllowedEmailServiceTests`,
`UserServiceTests` (`Fiolyn.Tests/Unit/Services/`); `AuthControllerTests`,
`UserControllerTests`, `AllowedEmailControllerTests` (`Fiolyn.Tests/Unit/Controllers/`).

## Configuration

`GoogleAuth:ClientId` and `Jwt:SigningKey` (+ issuer/audience/lifetimes) — see
`docs/ConfigurationSettingSetup.md` for the full key list and binding classes.
