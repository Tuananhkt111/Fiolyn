# Authentication & user management

Google SSO login, Fiolyn-signed JWTs, refresh-token rotation, the email whitelist that gates
account creation, and admin user management.

> **Canonical rules:** [rules/auth.md](../rules/auth.md) and
> [rules/access-control.md](../rules/access-control.md)

Routes, parameters and request bodies: [`openapi.json`](openapi.json) (`Auth`, `User`,
`AllowedEmail` tags).

## Identity model

- **`AppUser`** — created on first successful Google login. Carries `Email` (normalized through
  `AppUser.NormalizeEmail`), `GoogleSub`, `DisplayName`, `PictureUrl`, `IsAdmin`, `LastLoginAt`
  and the optional `TelegramId`.
- **`AllowedEmail`** — admin-managed whitelist. A Google login is rejected unless the account's
  email is present, and a refresh is refused once it has been removed.
- **`RefreshToken`** — opaque, persisted as a SHA-256 hash, revoked rather than soft-deleted.

`AppUser.TelegramId` doubles as the Telegram-ingest whitelist — see
[`telegram-ingest.md`](telegram-ingest.md).

## Login

1. The web client obtains a Google ID token via Google Identity Services.
2. `POST /api/auth/google` — `GoogleTokenVerifier` validates it against `GoogleAuth:ClientId`.
3. The email is checked against `AllowedEmail`. Not whitelisted → rejected.
4. `AppUser` upsert, by a merged lookup on `GoogleSub` **or** `Email`.
5. `JwtService` issues an HS256 access token plus an opaque refresh token — 64 random bytes,
   returned raw exactly once and stored only as a hash.

The user upsert and the refresh-token insert land in a **single** `SaveChangesAsync`.
`IssueTokensAsync` stages but does not save; each caller flushes once, so a partial login cannot
leave a token without its user.

## Token contract

`/api/auth/google` and `/api/auth/refresh` both return:

```json
{ "accessToken": "<jwt>", "refreshToken": "<opaque>", "expiresAt": "<utc>" }
```

`expiresAt` describes the **access** token. The refresh token's own lifetime is not returned — it
defaults to 30 days (`Jwt:RefreshTokenExpiryDays`) against the access token's 15 minutes
(`Jwt:AccessTokenExpiryMinutes`).

The access token is HS256 and carries:

| Claim | Value |
|---|---|
| `sub` | `AppUser.Id` — read it via `User.GetUserId()`, never by hand |
| `email` | Normalized email |
| `jti` | Unique per token |
| `isAdmin` | `"true"` or `"false"` — **a string, not a boolean** |
| `name` | `DisplayName`, omitted when unset |

`isAdmin` being a string matters: the `Admin` policy compares against `AuthConstants.AdminClaimTrueValue`,
and a client parsing the JWT must compare `"true"` rather than expect a JSON boolean.

The refresh token is opaque — 64 random bytes, not a JWT. It carries no claims and cannot be
inspected; the server stores only its SHA-256 hash.

## Token rotation and logout

`POST /api/auth/refresh` rotates atomically — the new token is inserted and the old one revoked in
one save.

Two protections sit on top:

- **Reuse detection.** Replaying an already-revoked token revokes every active refresh token of
  that user, on the assumption that a leaked token is being used in parallel with the legitimate
  one. `ReplacedByTokenHash` records each rotation but is not read by this check.
- **Whitelist re-check.** A refresh for a user whose email is no longer in `AllowedEmail` is refused
  with 401 and issues nothing, so removing an address ends its sessions within one access-token
  lifetime rather than the refresh token's 30 days.
- **Concurrency.** `RevokedAt` carries `[ConcurrencyCheck]`, so EF emits
  `UPDATE … WHERE Id = @id AND RevokedAt IS NULL`. Two concurrent refreshes on the same token race;
  the loser's save throws and its half-staged replacement rolls back with the same transaction,
  surfacing as 401.

`POST /api/auth/logout` sets `RevokedAt` on the supplied token and requires a valid access token.
The token must belong to the caller; someone else's token is answered with 401, exactly like an
unknown one, so the call reveals nothing about it.

## Authorization

Class-level `[Authorize]` on every protected controller, plus a named `Admin` policy requiring an
`isAdmin=true` claim.

- Reference the policy and claim through `AuthConstants` — never hardcode `"Admin"`, `"isAdmin"`
  or `"true"`.
- Read the caller's id through `User.GetUserId()` / `User.TryGetUserId()`, never
  `FindFirst(...)` directly.
- Inside a controller, `User` is the caller's `ClaimsPrincipal`. The entity is `AppUser`. They are
  different things and the names collide easily.

Cross-user admin operations live in separate `Admin<X>Controller` classes under `/api/admin/...`,
sharing the same service interface. See
[rules/access-control.md](../rules/access-control.md).

## Admin bootstrap

There is no auto-promotion code path anywhere:

1. Insert an `AllowedEmail` row for the target address.
2. Let that account log in once, which creates the `AppUser`.
3. `UPDATE "AppUser" SET "IsAdmin" = true` for that row.

That user can then manage the whitelist and other users' admin flags through the API.

## Conflicts

`TelegramId` and `AllowedEmail.Email` both carry unique indexes. A duplicate surfaces as 409 via
`ConflictException`, mapped from Postgres `23505`. The pre-check narrows the common case; the index
is the actual race-safe guarantee.

The indexes also cover soft-deleted rows. Re-adding a removed `AllowedEmail` restores that row (with
the new note and inviter) instead of inserting a second one. A soft-deleted `AppUser` who signs in
again is refused with 403, even if the address is still whitelisted: an admin's delete holds.

## Configuration

`Jwt:SigningKey` is mandatory and must be at least 32 characters — both `AuthServiceRegister` at
startup and `JwtService`'s constructor throw without it, deliberately, with no dev fallback. Full
key list: [`../guides/configuration.md`](../guides/configuration.md).

## Tests

`AuthServiceTests`, `JwtServiceTests`, `GoogleTokenVerifierTests`, `AllowedEmailServiceTests`,
`UserServiceTests` under `Fiolyn.Tests/Unit/Services/`; `AuthControllerTests`,
`UserControllerTests`, `AllowedEmailControllerTests` under `Fiolyn.Tests/Unit/Controllers/`.
