# Module 3 — Spend Accounts

Spend-account CRUD, membership-scoped access and the admin twin all work as FRS v1.0 states. Member management differs from the v1.0 text: members are added by **email**, not user id; adding an existing member is a no-op; the last member cannot be removed (400); and the member list runs a separate membership check before the read rather than one combined query. Git shows these behaviours date from `62fdfbe` (2026-04-24) and `09655d5` (2026-05-10), and no file in this module changed after v1.0 was written (2026-09-16) except a DTO move (`87a56ab`). So v1.0 misdescribed member management; the code did not move after it.

**Verdict:** 2 of 3 stories match; drift in US-10 (add by email, last-member guard).

## US-09 — Create and manage my spend accounts

**Code:** `SpendAccountController` · `SpendAccountService` · `ISpendAccountService` · `SpendAccount` · `SpendAccountMember` · `SpendAccountType` · `SpendAccountRequest` · `SpendAccountUpdateRequest` · `SpendAccountResponse` · `TransactionBalanceWriter` · tests tagged `[Trait( "Story", "US-09" )]`

**Tests:** `SpendAccountControllerTests.CreateAsync_PassesCallerIdToService_AndReturns201`, `SpendAccountControllerTests.GetAllAsync_PassesUserIdToService`, `SpendAccountControllerTests.GetByIdAsync_Missing_Returns404`, `SpendAccountControllerTests.GetByIdAsync_Existing_Returns200`, `SpendAccountControllerTests.UpdateAsync_Missing_Returns404`, `SpendAccountControllerTests.UpdateAsync_Existing_PassesScalarsToService_AndReturns200`, `SpendAccountControllerTests.DeleteAsync_Returns204`, `SpendAccountServiceTests.CreateAsync_AddsCreatorAsFirstMember_AndSaves`, `SpendAccountServiceTests.GetAllForUserAsync_FiltersByMembership`, `SpendAccountServiceTests.GetByIdForUserAsync_ReturnsNull_WhenCallerIsNotMember`, `SpendAccountServiceTests.UpdateAsync_AppliesNameAndType_DoesNotCallRepoUpdate_AndSaves`, `SpendAccountServiceTests.UpdateAsync_ReturnsNull_WhenCallerIsNotMember`, `SpendAccountServiceTests.SoftDeleteAsync_SoftDeletes_WhenCallerIsMember`, `SpendAccountServiceTests.SoftDeleteAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `SpendAccountServiceTests.SoftDeleteAsync_ThrowsBadRequest_WhenLinkedFromActiveBroker`, `SpendAccountServiceTests.SoftDeleteAsync_BlocksWhenActiveTransactionsExist`, `SpendAccountServiceTests.SoftDeleteAsync_BlocksWhenReferencedAsTransferDestination`

**Verdict: Matches.** Create, list, read, update and delete are membership-scoped; balance is untouched by `PUT`; referenced accounts cannot be deleted.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Create → 201; creator is a member; balance starts at the supplied value | Holds. | `Fiolyn/Controllers/SpendAccountController.cs:25-40`, `Fiolyn/Services/Internal/SpendAccountService.cs:25-35` |
| AC2 · Listing returns only accounts the caller is a member of | Holds. Paged, filtered on `Members.Any(...)`. | `Fiolyn/Services/Internal/SpendAccountService.cs:38-44` |
| AC3 · GET/PUT/DELETE on a non-member account → 404 | Holds. GET and PUT return `NotFound()` on a null result; DELETE throws `KeyNotFoundException`. | `Fiolyn/Controllers/SpendAccountController.cs:57-67`, `:76-85`, `Fiolyn/Services/Internal/SpendAccountService.cs:47-50`, `:68-71` |
| AC4 · PUT changes name/type only; balance unchanged | Holds. `SpendAccountUpdateRequest` has no `Balance`; the service sets only `Name` and `Type`. | `Fiolyn/Controllers/DTOs/Requests/SpendAccount/SpendAccountUpdateRequest.cs:6-8`, `Fiolyn/Services/Internal/SpendAccountService.cs:53-65` |
| AC5 · Delete while referenced by a broker or transaction → 400 | Holds. Checks active brokers, active transactions by `SpendAccountId`, and active transfers by `DestinationSpendAccountId`. | `Fiolyn/Services/Internal/SpendAccountService.cs:72-73`, `:217-245` |
| Type is Cash or Credit; Balance is a signed `long` with `[ConcurrencyCheck]` | Holds. | `Fiolyn/Enums/SpendAccountType.cs:11`, `:16`, `Fiolyn/Entities/SpendAccount.cs:37-38` |
| Concurrent balance-mutating writes → 409 | Holds. `DbUpdateConcurrencyException` maps to `ConflictException`. | `Fiolyn/Services/Internal/Helpers/TransactionBalanceWriter.cs:22-31` |

## US-10 — Share a spend account with another user

**Code:** `SpendAccountController` · `SpendAccountService` · `ISpendAccountService` · `SpendAccountMember` · `AddMemberRequest` · `SpendAccountMemberResponse` · tests tagged `[Trait( "Story", "US-10" )]`

**Tests:** `SpendAccountControllerTests.GetMembersAsync_Returns200WithMemberList`, `SpendAccountControllerTests.AddMemberAsync_PassesEmailToService_AndReturnsMemberList`, `SpendAccountControllerTests.RemoveMemberAsync_CallsServiceAndReturns204`, `SpendAccountServiceTests.GetMembersAsync_ReturnsMembersOrderedByJoinedAt_WhenCallerIsMember`, `SpendAccountServiceTests.GetMembersAsync_Throws_WhenCallerIsNotMember`, `SpendAccountServiceTests.AddMemberAsync_AddsNewMember_WhenTargetExists`, `SpendAccountServiceTests.AddMemberAsync_NormalizesEmailBeforeLookup`, `SpendAccountServiceTests.AddMemberAsync_IsIdempotent_WhenTargetIsAlreadyMember`, `SpendAccountServiceTests.AddMemberAsync_ThrowsKeyNotFound_WhenTargetEmailIsUnknown`, `SpendAccountServiceTests.AddMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `SpendAccountServiceTests.RemoveMemberAsync_RemovesMember_WhenMultipleRemain`, `SpendAccountServiceTests.RemoveMemberAsync_ThrowsBadRequest_WhenRemovingLastMember`, `SpendAccountServiceTests.RemoveMemberAsync_ThrowsKeyNotFound_WhenTargetIsNotMember`, `SpendAccountServiceTests.RemoveMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `SpendAccountServiceTests.GetMembersAsync_ChecksMembershipInTheSameQuery_NoSeparateMemberQuery`, `SpendAccountServiceTests.GetMembersAsync_AccountExistsButCallerIsNotAMember_ThrowsKeyNotFound`

**Verdict: Drifted.** All three ACs hold, but members are added by email (not user id) and the last member cannot be removed — both since `62fdfbe` (2026-04-24).

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · A member adds a user → that user immediately has full access | Holds. A join row is inserted; every user-scoped read filters on membership. | `Fiolyn/Services/Internal/SpendAccountService.cs:128-134`, `:41`, `:50` |
| AC2 · Non-member attempting member management → 404 | Holds for list, add and remove. | `Fiolyn/Services/Internal/SpendAccountService.cs:82-88`, `:104-115`, `:142-150` |
| AC3 · Removing a member revokes their access immediately | Holds. The join row is hard-deleted. | `Fiolyn/Services/Internal/SpendAccountService.cs:162-163` |
| Main flow · "A member adds another user by user id" | Differs: `AddMemberRequest` takes an **email**; the service normalizes it and looks the user up. Unknown email → 404. | `Fiolyn/Controllers/DTOs/Requests/Shared/AddMemberRequest.cs:9-13`, `Fiolyn/Services/Internal/SpendAccountService.cs:100-121` |
| Main flow · Adding a user who is already a member | Not in v1.0. Idempotent: returns the current list (200), no error. | `Fiolyn/Services/Internal/SpendAccountService.cs:123-126` |
| Main flow · A member removes a member (including themselves) | Holds, with a guard not in v1.0: removing the **last** member → 400 "delete the account instead". | `Fiolyn/Services/Internal/SpendAccountService.cs:147-160` |
| List members with email / display name | Holds. | `Fiolyn/Services/Internal/SpendAccountService.cs:90-96`, `Fiolyn/Controllers/DTOs/Responses/SpendAccount/SpendAccountMemberResponse.cs` |
| BR · Caller-membership check baked into the same query that fetches/mutates membership | Differs for the list: `GetMembersAsync` runs a separate `AnyAsync` check, then a second query (since `09655d5`, 2026-05-10). Add and remove do it in one query. `docs/rules/access-control.md` states the one-query rule too. | `Fiolyn/Services/Internal/SpendAccountService.cs:82-96`, `:104-110`, `:142-145` |
| BR · No invite/accept flow | Holds. | `Fiolyn/Controllers/SpendAccountController.cs:112-116` |
| BR · Join rows hard-deleted | Holds. `SpendAccountMember` is not `ISoftDelete`; removal calls `Delete`. | `Fiolyn/Entities/SpendAccountMember.cs:13`, `Fiolyn/Services/Internal/SpendAccountService.cs:162` |

## US-11 — Administer any spend account (admin)

**Code:** `AdminSpendAccountController` · `SpendAccountService` · `ISpendAccountService` · `AdminSpendAccountCreateRequest` · `SpendAccountUpdateRequest` · `AdminSpendAccountResponse` · `SpendAccountMemberResponse` · `AuthConstants` · tests tagged `[Trait( "Story", "US-11" )]`

**Tests:** `AdminSpendAccountControllerTests.GetAllAsync_NoFilter_ReturnsAdminResponseListWithMembers`, `AdminSpendAccountControllerTests.GetAllAsync_WithUserIdFilter_PassesFilter`, `AdminSpendAccountControllerTests.GetByIdAsync_Missing_Returns404`, `AdminSpendAccountControllerTests.GetByIdAsync_Existing_Returns200WithAdminResponse`, `AdminSpendAccountControllerTests.CreateAsync_UsesAppUserIdFromRequest_AndReturns201`, `AdminSpendAccountControllerTests.UpdateAsync_Missing_Returns404`, `AdminSpendAccountControllerTests.UpdateAsync_Existing_PassesScalarsToService_AndReturns200`, `AdminSpendAccountControllerTests.DeleteAsync_Returns204_AndCallsUnscopedSoftDelete`, `SpendAccountServiceTests.GetAllAsync_NoFilter_ReturnsAllAccountsOrderedByName`, `SpendAccountServiceTests.GetAllAsync_WithUserIdFilter_ReturnsOnlyAccountsUserIsMemberOf`, `SpendAccountServiceTests.GetByIdAsync_ReturnsAccount_RegardlessOfMembership`, `SpendAccountServiceTests.GetByIdAsync_ReturnsNull_WhenNotFound`, `SpendAccountServiceTests.AdminUpdateAsync_AppliesNameAndType_AndSaves`, `SpendAccountServiceTests.AdminUpdateAsync_ReturnsNull_WhenMissing`, `SpendAccountServiceTests.AdminSoftDeleteAsync_SoftDeletes_WhenFound`, `SpendAccountServiceTests.AdminSoftDeleteAsync_Propagates_KeyNotFoundException_FromRepository`, `SpendAccountServiceTests.AdminSoftDeleteAsync_ThrowsBadRequest_WhenLinkedFromActiveBroker`, `AuthorizationTests.AdminOnly_NonAdminToken_Returns403`, `AuthorizationTests.AdminOnly_AdminToken_IsNeitherUnauthorizedNorForbidden`

**Verdict: Matches.** All five admin endpoints sit behind the `Admin` policy and ignore membership.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Non-admin → 403 on all `/api/admin/spend-accounts` endpoints | Holds via class-level `Admin` policy. No test covers the attribute. | `Fiolyn/Controllers/AdminSpendAccountController.cs:17` |
| AC2 · Admin can read/modify accounts regardless of membership | Holds. Admin service methods have no membership filter. | `Fiolyn/Services/Internal/SpendAccountService.cs:167-215` |
| GET (paged), GET /{id}, POST (`AdminSpendAccountCreateRequest`), PUT /{id}, DELETE /{id} | Holds. POST makes `AppUserId` the first member; PUT reuses `SpendAccountUpdateRequest` (no balance). `GET` also accepts an optional `?userId=` filter (not in v1.0). | `Fiolyn/Controllers/AdminSpendAccountController.cs:24-92`, `Fiolyn/Controllers/DTOs/Requests/SpendAccount/AdminSpendAccountCreateRequest.cs:9-13` |
| Responses enriched with owner/member information | Holds for members (id, email, display name, joined-at). There is no separate owner field. | `Fiolyn/Controllers/DTOs/Responses/SpendAccount/AdminSpendAccountResponse.cs:8-27`, `Fiolyn/Services/Internal/SpendAccountService.cs:186-190` |
| Admin delete follows the same reference guard as US-09 | Holds (not stated in v1.0). | `Fiolyn/Services/Internal/SpendAccountService.cs:193-200` |
| Not in v1.0 · Admin create with an `AppUserId` that does not exist | Not verified. No existence check before insert, so the FK would fail at save and surface as 500. Inferred from code; no test. | `Fiolyn/Services/Internal/SpendAccountService.cs:25-35` |

---
[← Module 2](module-2-users-profile.md) · [Index](README.md) · [Module 4 →](module-4-broker-accounts.md)
