# Module 1 — Authentication & Access

Google SSO login, refresh-token rotation, logout and the admin-managed email whitelist all work as FRS v1.0 states. Every acceptance criterion holds in the code. Two v1.0 descriptions are looser than the code: reuse detection revokes every active refresh token of the user rather than walking the `ReplacedByTokenHash` chain, and every Google login (not only the first) re-checks the whitelist. None of the files in this module changed after v1.0 was written (2026-09-16). Authorization outcomes (401/403) come from `[Authorize]` attributes; no test drives the auth pipeline, so those rows rest on the attributes alone.

**Verdict:** 4 of 4 stories match; no drift. One gap outside the v1.0 criteria: a removed whitelist email cannot be added back (409).

## US-01 — Log in with Google

**Code:** `AuthController` · `AuthService` · `IAuthService` · `GoogleTokenVerifier` · `IGoogleTokenVerifier` · `GooglePayload` · `JwtService` · `IJwtService` · `AuthServiceRegister` · `AppUser` · `AllowedEmail` · `RefreshToken` · `GoogleLoginRequest` · `AuthResponse` · tests tagged `[Trait( "Story", "US-01" )]`

**Tests:** `AuthServiceTests.LoginWithGoogleAsync_EmailNotWhitelisted_ThrowsForbidden`, `AuthServiceTests.LoginWithGoogleAsync_FirstLogin_CreatesNonAdminUser`, `AuthServiceTests.LoginWithGoogleAsync_ExistingUser_UpdatesProfileAndLoginTimestamp`, `AuthControllerTests.LoginWithGoogleAsync_Returns200WithTokens`, `GoogleTokenVerifierTests.VerifyAsync_EmptyToken_Throws`, `GoogleTokenVerifierTests.VerifyAsync_MalformedToken_ThrowsUnauthorized`, `JwtServiceTests.Ctor_MissingSigningKey_Throws`, `JwtServiceTests.Ctor_ShortSigningKey_Throws`, `JwtServiceTests.CreateAccessToken_IncludesExpectedClaims`, `JwtServiceTests.CreateRefreshToken_RawAndHashAreConsistent`

**Verdict: Matches.** Login verifies the Google token, checks the whitelist, upserts the user and issues both tokens in one save.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Valid Google token + whitelisted email → 200 with access + refresh tokens; AppUser exists | Holds. User is added or updated, token staged, one `SaveAsync`. | `Fiolyn/Controllers/AuthController.cs:18-23`, `Fiolyn/Services/Internal/AuthService.cs:35-65` |
| AC2 · Valid token, email not whitelisted → rejected, no AppUser created | Holds. Throws `ForbiddenAccessException` (403) before the user lookup, so nothing is added. | `Fiolyn/Services/Internal/AuthService.cs:28-33`, `Fiolyn/Infrastructure/GlobalExceptionHandler.cs:22` |
| AC3 · Invalid/expired Google token → 401 | Holds. Empty token, `InvalidJwtException`, or an unverified/missing email all throw `UnauthorizedAccessException`. Audience is `GoogleAuth:ClientId`. | `Fiolyn/Services/External/Google/GoogleTokenVerifier.cs:18-21`, `:31-47` |
| AC4 · Second login reuses the existing AppUser | Holds. Lookup by `GoogleSub` or normalized `Email`; existing row is updated, not re-added. | `Fiolyn/Services/Internal/AuthService.cs:35-36`, `:52-60` |
| First login creates AppUser from Google profile, `IsAdmin = false` | Holds. | `Fiolyn/Services/Internal/AuthService.cs:38-51` |
| Later logins update `LastLoginAt` and reconcile the profile | Holds. Email, GoogleSub, DisplayName and PictureUrl are overwritten when Google supplies them. | `Fiolyn/Services/Internal/AuthService.cs:54-58`, `:62` |
| Access token HS256; refresh token 64 random bytes, stored only as SHA-256 | Holds. | `Fiolyn/Services/Internal/JwtService.cs:21`, `:47`, `:63`, `:73`, `Fiolyn/Services/Internal/AuthService.cs:144-157` |
| BR · Email normalized (trim + lowercase) via `AppUser.NormalizeEmail` | Holds. | `Fiolyn/Entities/AppUser.cs:29`, `:104`, `Fiolyn/Services/Internal/AuthService.cs:26` |
| BR · `Jwt:SigningKey` mandatory, ≥ 32 chars; startup and `JwtService` both throw | Holds. | `Fiolyn/ServiceRegisters/AuthServiceRegister.cs:29-34`, `Fiolyn/Services/Internal/JwtService.cs:77-86` |
| BR · Email absent from AllowedEmail cannot create or access an account | Holds for login: the whitelist is checked on every Google login. Note: `/api/auth/refresh` does not re-check the whitelist, so a user removed from it keeps refreshing existing tokens. | `Fiolyn/Services/Internal/AuthService.cs:28-33`, `:70-115` |

## US-02 — Refresh an access token

**Code:** `AuthController` · `AuthService` · `IAuthService` · `JwtService` · `IJwtService` · `RefreshToken` · `RefreshTokenRequest` · `AuthResponse` · tests tagged `[Trait( "Story", "US-02" )]`

**Tests:** `AuthServiceTests.RefreshAsync_ValidToken_RotatesAndReturnsNewPair`, `AuthServiceTests.RefreshAsync_RevokedTokenReplay_RevokesChainAndThrows`, `AuthServiceTests.RefreshAsync_ConcurrentRotation_LoserGets401_AndDoesNotKeepNewToken`, `AuthServiceTests.RefreshAsync_ExpiredToken_Throws`, `AuthServiceTests.RefreshAsync_UnknownToken_Throws`, `AuthServiceTests.RefreshAsync_SoftDeletedUser_Throws`, `AuthControllerTests.RefreshAsync_Returns200WithTokens`

**Verdict: Matches.** Rotation is atomic, replay and expiry give 401, and the concurrency guard is in place.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Valid, unrevoked token → 200 with new pair; old token revoked | Holds. New token staged, old one gets `RevokedAt` and `ReplacedByTokenHash`, one `SaveAsync`. | `Fiolyn/Controllers/AuthController.cs:29-34`, `Fiolyn/Services/Internal/AuthService.cs:100-113` |
| AC2 · Replaying a revoked token → 401 and the whole chain revoked | Holds, by a broader mechanism: the code revokes **every** active refresh token of that user with one `ExecuteUpdateAsync`. `ReplacedByTokenHash` is written but never read. | `Fiolyn/Services/Internal/AuthService.cs:87-93`, `:159-165`, `Fiolyn/Entities/RefreshToken.cs:59` |
| AC3 · Two concurrent refreshes → one succeeds, the other 401 | Holds in code: `RevokedAt` is `[ConcurrencyCheck]`; `DbUpdateConcurrencyException` maps to 401. The test simulates the exception; no test runs a real database race. | `Fiolyn/Entities/RefreshToken.cs:51-52`, `Fiolyn/Services/Internal/AuthService.cs:105-113` |
| Supplied token hashed (SHA-256) and matched against the stored hash | Holds. | `Fiolyn/Services/Internal/AuthService.cs:77-80`, `Fiolyn/Services/Internal/JwtService.cs:73` |
| BR · Expired tokens rejected → 401 | Holds. | `Fiolyn/Services/Internal/AuthService.cs:82-85` |
| Not in v1.0 · Refresh for a soft-deleted user | Returns 401. | `Fiolyn/Services/Internal/AuthService.cs:95-98` |

## US-03 — Log out

**Code:** `AuthController` · `AuthService` · `IAuthService` · `RefreshToken` · `RefreshTokenRequest` · tests tagged `[Trait( "Story", "US-03" )]`

**Tests:** `AuthServiceTests.LogoutAsync_RevokesToken`, `AuthServiceTests.LogoutAsync_AlreadyRevoked_IsNoOp`, `AuthServiceTests.LogoutAsync_UnknownToken_Throws`, `AuthControllerTests.LogoutAsync_Returns204`

**Verdict: Matches.** Logout requires a bearer token, sets `RevokedAt` and returns 204.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Authenticated logout with a valid token → 204; token revoked and cannot refresh | Holds. A revoked token then fails refresh (and triggers reuse detection). | `Fiolyn/Controllers/AuthController.cs:40-45`, `Fiolyn/Services/Internal/AuthService.cs:135-141`, `:87-93` |
| AC2 · Unauthenticated logout → 401 | Holds via `[Authorize]` on the action. No test covers the attribute. | `Fiolyn/Controllers/AuthController.cs:41` |
| BR · Revocation is explicit (`RevokedAt`); never soft-deleted | Holds. `RefreshToken` implements `IAuditable` only, not `ISoftDelete`. | `Fiolyn/Entities/RefreshToken.cs:12`, `:51-52` |
| Not in v1.0 · Unknown token / already-revoked token / token owner | Unknown token → 401; already revoked → 204, no change. The service does not check that the token belongs to the caller. | `Fiolyn/Services/Internal/AuthService.cs:130-141` |

## US-04 — Manage the email whitelist (admin)

**Code:** `AllowedEmailController` · `AllowedEmailService` · `IAllowedEmailService` · `AllowedEmail` · `AllowedEmailRequest` · `AllowedEmailResponse` · `AuthConstants` · `AuthService` · tests tagged `[Trait( "Story", "US-04" )]`

**Tests:** `AllowedEmailServiceTests.CreateAsync_NormalizesEmailAndPersists`, `AllowedEmailServiceTests.CreateAsync_DuplicateEmail_Throws`, `AllowedEmailServiceTests.CreateAsync_RaceLost_UniqueViolationMappedToConflict`, `AllowedEmailServiceTests.SoftDeleteAsync_Existing_SoftDeletes`, `AllowedEmailServiceTests.SoftDeleteAsync_Missing_ThrowsKeyNotFound`, `AllowedEmailControllerTests.GetAllAsync_Returns200WithMappedList`, `AllowedEmailControllerTests.CreateAsync_Returns201WithInviterAttributed`, `AllowedEmailControllerTests.DeleteAsync_Returns204`

**Verdict: Matches.** All four ACs hold; a removed email cannot be re-added through the API (not a v1.0 criterion).

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Non-admin on any `/api/allowed-emails` endpoint → 403 | Holds via class-level `Admin` policy. No test covers the attribute. | `Fiolyn/Controllers/AllowedEmailController.cs:17`, `Fiolyn/ServiceRegisters/AuthServiceRegister.cs:55-57` |
| AC2 · Adding a new email → 201; email may now log in | Holds. `CreatedAtRoute` → 201; login checks the same table. | `Fiolyn/Controllers/AllowedEmailController.cs:33-38`, `Fiolyn/Services/Internal/AuthService.cs:28-29` |
| AC3 · Adding an email already whitelisted → 409 | Holds. Pre-check plus Postgres `23505` both map to `ConflictException`. | `Fiolyn/Services/Internal/AllowedEmailService.cs:31-36`, `:50-54` |
| AC4 · Deleting an entry → 204; future first-time logins blocked | Holds, and stricter: the entry is soft-deleted and every later Google login for that email is refused, not only first-time ones. Existing refresh tokens keep working (see US-01). | `Fiolyn/Controllers/AllowedEmailController.cs:43-48`, `Fiolyn/Services/Internal/AllowedEmailService.cs:62-68`, `Fiolyn/Services/Internal/AuthService.cs:28-33` |
| List as `PagedResult` | Holds. | `Fiolyn/Services/Internal/AllowedEmailService.cs:19-24` |
| BR · Emails normalized like AppUser | Holds. | `Fiolyn/Entities/AllowedEmail.cs:27`, `Fiolyn/Services/Internal/AllowedEmailService.cs:29` |
| BR · Admin bootstrap is manual; no auto-promotion | Holds. The `InitialAdmin` seeder was removed in `74bed2c` (2026-04-20); no code sets `IsAdmin = true` except the admin `PATCH /api/users/{id}`. | `Fiolyn/Services/Internal/AuthService.cs:46`, `Fiolyn/Services/Internal/UserService.cs:38-45` |
| Not in v1.0 · Re-adding a removed email | Returns 409. The pre-check skips soft-deleted rows (query filter) but the unique index on `Email` has no filter, so the insert hits `23505`. Inferred from code; no test. | `Fiolyn/Entities/AllowedEmail.cs:10-11`, `Fiolyn/Data/AppDbContext.cs:123`, `Fiolyn/Services/Internal/AllowedEmailService.cs:31-54` |

---
[Index](README.md) · [Module 2 →](module-2-users-profile.md)
