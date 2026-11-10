# Module 2 — Users & Profile

Profile reads, Telegram-ID linking and admin user management are in place. Two v1.0 statements do not match the code: `PATCH /api/users/me` edits only the display name (email is not editable, so the 409 email-conflict path does not exist), and `DELETE /api/users/me/telegram-id` returns 200 with the profile, not 204. Git shows both behaviours date from the first commits for these endpoints (`cdf57e4`, 2026-04-20 and `95cc427`, 2026-04-25) and no file in this module changed after v1.0 was written (2026-09-16). So v1.0 misdescribed them; the code did not move.

**Verdict:** 2 of 4 stories match; partial in US-06 (email not editable); drift in US-07 (unlink returns 200).

## US-05 — View my profile

**Code:** `UserController` · `UserService` · `IUserService` · `UserResponse` · `AppUser` · `ClaimsPrincipalExtensions` · tests tagged `[Trait( "Story", "US-05" )]`

**Tests:** `UserControllerTests.GetMeAsync_Returns200WithCallerProfile`, `UserServiceTests.GetByIdAsync_Missing_ThrowsKeyNotFound`, `AuthorizationTests.Enumeration_FindsEndpointsAndEveryAdminGroup`, `AuthorizationTests.Protected_WithoutToken_Returns401`, `AuthorizationTests.AppRoutes_MatchOpenApiOperations`, `AuthorizationTests.NonAdminEndpoint_NonAdminToken_IsNeitherUnauthorizedNorForbidden`

**Verdict: Matches.** `GET /api/users/me` reads the caller id from the token and returns their profile.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Authenticated `GET /me` → 200 with the caller's fields | Holds. `UserResponse` carries email, display name, picture, Telegram ID and admin flag, plus `Id`, `LastLoginAt`, `Created`. | `Fiolyn/Controllers/UserController.cs:23-27`, `Fiolyn/Controllers/DTOs/Responses/User/UserResponse.cs` |
| AC2 · Unauthenticated → 401 | Holds via class-level `[Authorize]`. No test covers the attribute. | `Fiolyn/Controllers/UserController.cs:17` |
| AC3 · A user can only read their own profile via `/me` | Holds. The id comes from `User.GetUserId()`; there is no id parameter. | `Fiolyn/Controllers/UserController.cs:26` |
| BR · Cross-user reads are admin-only (US-08) | Holds. The only cross-user read is the admin list `GET /api/users`; there is no `GET /api/users/{id}`. | `Fiolyn/Controllers/UserController.cs:63-68` |

## US-06 — Edit my profile

**Code:** `UserController` · `UserService` · `IUserService` · `UpdateMeRequest` · `UserResponse` · `AppUser` · tests tagged `[Trait( "Story", "US-06" )]`

**Tests:** `UserControllerTests.UpdateMeAsync_DelegatesCallerIdAndReturns200`, `UserServiceTests.UpdateDisplayNameAsync_UpdatesAndSaves`

**Verdict: Partly.** Display name edits work and `IsAdmin` is not editable, but email cannot be changed, so AC2 has no code path.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Editing display name / email → 200 with updated values | Differs: display name only. `UpdateMeRequest` has a single `DisplayName` field; the service sets only `DisplayName`. Unchanged since `cdf57e4` (2026-04-20). | `Fiolyn/Controllers/DTOs/Requests/User/UpdateMeRequest.cs:8-9`, `Fiolyn/Controllers/UserController.cs:33-37`, `Fiolyn/Services/Internal/UserService.cs:28-35` |
| AC2 · Changing email to one already used → 409 | Missing. No endpoint changes `AppUser.Email` after creation (only Google login reconciles it). | `Fiolyn/Controllers/DTOs/Requests/User/UpdateMeRequest.cs:8-9`, `Fiolyn/Services/Internal/AuthService.cs:54` |
| AC3 · A user cannot change their own `IsAdmin` here | Holds. `UpdateMeRequest` has no `IsAdmin`. | `Fiolyn/Controllers/DTOs/Requests/User/UpdateMeRequest.cs:8-9` |
| BR · Email uniqueness enforced; email normalized | Not applicable on this endpoint (email not editable). The unique index and normalizing setter exist on `AppUser`. | `Fiolyn/Entities/AppUser.cs:10`, `:29` |
| Profile Fields Reference · Email "✓ (via PATCH /me)" | Differs: Email is not user-editable. | `Fiolyn/Controllers/DTOs/Requests/User/UpdateMeRequest.cs:8-9` |
| Not in v1.0 · A user-set display name survives the next login? | No. Login overwrites `DisplayName` whenever Google sends a name. | `Fiolyn/Services/Internal/AuthService.cs:56` |

## US-07 — Link / unlink my Telegram ID

**Code:** `UserController` · `UserService` · `IUserService` · `SetTelegramIdRequest` · `UserResponse` · `AppUser` · `TelegramMessageService` · tests tagged `[Trait( "Story", "US-07" )]`

**Tests:** `UserControllerTests.SetMyTelegramIdAsync_DelegatesCallerIdAndReturns200`, `UserControllerTests.ClearMyTelegramIdAsync_DelegatesCallerIdWithNullAndReturns200`, `UserServiceTests.UpdateTelegramIdAsync_SetsAndSaves`, `UserServiceTests.UpdateTelegramIdAsync_NullClearsAndSkipsUniquenessCheck`, `UserServiceTests.UpdateTelegramIdAsync_TakenByOtherUser_ThrowsConflictAndDoesNotSave`, `UserServiceTests.UpdateTelegramIdAsync_RaceLost_UniqueViolationMappedToConflict`, `UserServiceTests.UpdateTelegramIdAsync_SelfReassign_IsNotAConflict`, `TelegramMessageServiceTests.ProcessWebhookAsync_TelegramIdNotLinked_ReturnsSkippedAndDoesNotCallNotion`

**Verdict: Drifted.** Linking and the 409 work, but unlinking returns 200 with the profile body, not 204 (since `95cc427`, 2026-04-25).

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Linking an unused Telegram ID → 200/204; user can ingest via Telegram | Holds. Returns 200 with `UserResponse`; the webhook resolves the sender by `AppUser.TelegramId`. | `Fiolyn/Controllers/UserController.cs:43-47`, `Fiolyn/Services/External/Telegram/TelegramMessageService.cs:38-44` |
| AC2 · Linking an ID used by another user → 409 | Holds. Pre-check plus Postgres `23505` both map to `ConflictException`. | `Fiolyn/Services/Internal/UserService.cs:53-60`, `:69-73`, `Fiolyn/Entities/AppUser.cs:12` |
| AC3 · Clearing the ID → 204; future messages from that ID ignored | Differs: returns **200** with `UserResponse`, not 204. The "ignored" part holds: an unlinked sender is skipped as "User not allowed". | `Fiolyn/Controllers/UserController.cs:53-57`, `Fiolyn/Services/External/Telegram/TelegramMessageService.cs:38-44` |
| BR · `TelegramId` is the Telegram whitelist | Holds. | `Fiolyn/Entities/AppUser.cs:61`, `Fiolyn/Services/External/Telegram/TelegramMessageService.cs:38-44` |
| BR · Duplicate surfaces as 409, race-safe via unique index | Holds. | `Fiolyn/Entities/AppUser.cs:12`, `Fiolyn/Services/Internal/UserService.cs:69-73` |

## US-08 — Manage users and roles (admin)

**Code:** `UserController` · `UserService` · `IUserService` · `UpdateRoleRequest` · `SetTelegramIdRequest` · `UserResponse` · `AppUser` · `JwtService` · `AuthConstants` · tests tagged `[Trait( "Story", "US-08" )]`

**Tests:** `UserControllerTests.GetAllAsync_Returns200WithMappedList`, `UserControllerTests.SetAdminAsync_DelegatesAndReturns200`, `UserControllerTests.SetTelegramIdAsync_DelegatesToRouteIdAndReturns200`, `UserControllerTests.ClearTelegramIdAsync_DelegatesToRouteIdWithNullAndReturns200`, `UserControllerTests.DeleteAsync_Returns204`, `UserServiceTests.SetAdminAsync_TogglesFlag`, `UserServiceTests.SoftDeleteAsync_Existing_SoftDeletes`, `UserServiceTests.UpdateTelegramIdAsync_TakenByOtherUser_ThrowsConflictAndDoesNotSave`, `JwtServiceTests.CreateAccessToken_IncludesExpectedClaims`, `JwtServiceTests.CreateAccessToken_NonAdmin_SetsIsAdminFalse`, `AuthorizationTests.AdminOnly_NonAdminToken_Returns403`, `AuthorizationTests.AdminOnly_AdminToken_IsNeitherUnauthorizedNorForbidden`, `AuthorizationTests.NonAdminEndpoint_NonAdminToken_IsNeitherUnauthorizedNorForbidden`

**Verdict: Matches.** All five admin endpoints exist behind the `Admin` policy and behave as stated.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Non-admin on any admin user endpoint → 403 | Holds via per-action `Admin` policy on all five endpoints. No test covers the attributes. | `Fiolyn/Controllers/UserController.cs:64`, `:75`, `:86`, `:97`, `:108` |
| AC2 · Admin promotes a user → that user's later tokens carry the admin claim | Holds. `PATCH /{id}` sets `IsAdmin`; the claim is read from `user.IsAdmin` at each token issue, including refresh. Existing access tokens keep the old claim until they expire. | `Fiolyn/Services/Internal/UserService.cs:38-45`, `Fiolyn/Services/Internal/JwtService.cs:37`, `Fiolyn/Services/Internal/AuthService.cs:100` |
| AC3 · Admin links a Telegram ID already in use → 409 | Holds. Same service method as US-07. | `Fiolyn/Controllers/UserController.cs:85-90`, `Fiolyn/Services/Internal/UserService.cs:53-73` |
| AC4 · Admin soft-deletes a user → excluded from active queries | Holds. `SoftDelete` plus the `AppUser` query filter; the member join rows of a deleted user are also filtered out. | `Fiolyn/Services/Internal/UserService.cs:81-86`, `Fiolyn/Data/AppDbContext.cs:122`, `:133` |
| `GET /api/users` lists as `PagedResult` | Holds. | `Fiolyn/Services/Internal/UserService.cs:20-25` |
| Admin unlink `DELETE /{id}/telegram-id` | Returns 200 with `UserResponse` (v1.0 gives no code). | `Fiolyn/Controllers/UserController.cs:96-101` |
| Not in v1.0 · Soft-deleted user signs in again with Google | Not verified. The lookup skips soft-deleted rows, so login tries to insert a second `AppUser` with the same `Email`/`GoogleSub`; the unique indexes have no filter and `AuthService` does not catch `DbUpdateException`, which would give 500. Inferred from code; no test. Refresh for that user returns 401. | `Fiolyn/Services/Internal/AuthService.cs:35-51`, `:95-98`, `Fiolyn/Entities/AppUser.cs:10-11` |

---
[← Module 1](module-1-authentication.md) · [Index](README.md) · [Module 3 →](module-3-spend-accounts.md)
