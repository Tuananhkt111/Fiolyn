# Module 4 — Broker Accounts

A `BrokerAccount` is a trading account. It is shared through `BrokerAccountMember` rows and is always linked to one `SpendAccount` through a required FK. The user-scoped controller and the admin controller behave as v1.0 describes. No production code in this module has changed since v1.0 (last change: `87a56ab`, 2026-09-16, which only moved `AddMemberRequest` into `Requests/Shared`). The one difference from the v1.0 text is older than v1.0: members are added by email, not by user id.

**Verdict:** 3 of 3 stories match; no drift. US-13's main-flow text says members are added "by user id"; the code adds them by email and has done so since 2026-04-24.

## US-12 — Create and manage my broker accounts

**Code:** `BrokerAccountController` · `BrokerAccountService` · `IBrokerAccountService` · `BrokerAccount` · `BrokerAccountMember` · `BrokerAccountRequest` · `BrokerAccountResponse` · tests tagged `[Trait( "Story", "US-12" )]`

**Tests:** `BrokerAccountServiceTests.CreateAsync_AddsCreatorAsFirstMember_AndSaves`, `BrokerAccountServiceTests.CreateAsync_ThrowsKeyNotFound_WhenCallerIsNotMemberOfSpendAccount`, `BrokerAccountServiceTests.CreateAsync_ThrowsKeyNotFound_WhenSpendAccountIsMissingOrSoftDeleted`, `BrokerAccountServiceTests.GetAllForUserAsync_FiltersByMembership`, `BrokerAccountServiceTests.GetAllForUserAsync_OrdersByName`, `BrokerAccountServiceTests.GetByIdForUserAsync_ReturnsAccount_WhenCallerIsMember`, `BrokerAccountServiceTests.GetByIdForUserAsync_ReturnsNull_WhenCallerIsNotMember`, `BrokerAccountServiceTests.ExistsForUserAsync_True_WhenMember`, `BrokerAccountServiceTests.ExistsForUserAsync_False_WhenNotMember`, `BrokerAccountServiceTests.UpdateAsync_ReturnsNull_WhenCallerIsNotMember`, `BrokerAccountServiceTests.UpdateAsync_DoesNotRevalidateSpendMembership_WhenSpendAccountIdUnchanged`, `BrokerAccountServiceTests.UpdateAsync_RevalidatesSpendMembership_AndRefetches_WhenSpendAccountIdChanges`, `BrokerAccountServiceTests.UpdateAsync_ThrowsKeyNotFound_WhenChangedSpendAccountIsNotMember`, `BrokerAccountServiceTests.SoftDeleteAsync_SoftDeletes_WhenCallerIsMember`, `BrokerAccountServiceTests.SoftDeleteAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `BrokerAccountControllerTests.CreateAsync_PassesCallerIdToService_AndReturns201`, `BrokerAccountControllerTests.GetAllAsync_PassesUserIdToService`, `BrokerAccountControllerTests.GetByIdAsync_Missing_Returns404`, `BrokerAccountControllerTests.GetByIdAsync_Existing_Returns200`, `BrokerAccountControllerTests.ExistsAsync_True_Returns200`, `BrokerAccountControllerTests.ExistsAsync_False_Returns404`, `BrokerAccountControllerTests.UpdateAsync_Missing_Returns404`, `BrokerAccountControllerTests.UpdateAsync_PassesScalarsToService_AndReturns200`, `BrokerAccountControllerTests.DeleteAsync_Returns204`

**Verdict: Matches.** Create, membership-scoped reads, update with a conditional spend-membership check, and soft delete all behave as v1.0 states.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Creating a broker linked to a spend account the caller is a member of → 201 | Holds. The service checks spend membership, adds the creator as first member and saves; the controller refetches and returns `CreatedAtRoute`. | `Fiolyn/Services/Internal/BrokerAccountService.cs:24-35`, `Fiolyn/Controllers/BrokerAccountController.cs:25-41` |
| AC2 · Linking to a spend account the caller is not a member of → rejected | Holds. Rejected with 404 (`KeyNotFoundException`), on create and on an FK change. | `Fiolyn/Services/Internal/BrokerAccountService.cs:247-257`, `:70-73` |
| AC3 · GET/PUT/DELETE on a broker the caller is not a member of → 404 | Holds. Every read and write filters on `Members.Any( m => m.AppUserId == userId )`; a null result becomes 404. HEAD also answers 404. | `Fiolyn/Services/Internal/BrokerAccountService.cs:38-58`, `:61-67`, `:88-94`; `Fiolyn/Controllers/BrokerAccountController.cs:60-63`, `:75`, `:86-89` |
| AC4 · Renaming without touching the FK skips the spend-membership check | Holds. The check runs only when `oldSpendId != spendAccountId`. | `Fiolyn/Services/Internal/BrokerAccountService.cs:69-73` |
| BR · When the FK changes, the write response is refetched through the read path | Holds. | `Fiolyn/Services/Internal/BrokerAccountService.cs:79-84` |
| BR · `SpendAccountId` is a required FK | Holds. `BrokerAccountRequest` requires both `Name` and `SpendAccountId`, so PUT is a full replace of the two fields, not a partial update. | `Fiolyn/Controllers/DTOs/Requests/BrokerAccount/BrokerAccountRequest.cs:10-12`, `Fiolyn/Entities/BrokerAccount.cs:36` |

## US-13 — Share a broker account

**Code:** `BrokerAccountController` · `BrokerAccountService` · `BrokerAccountMember` · `AddMemberRequest` · `BrokerAccountMemberResponse` · tests tagged `[Trait( "Story", "US-13" )]`

**Tests:** `BrokerAccountServiceTests.GetMembersAsync_ReturnsMembersOrderedByJoinedAt_WhenCallerIsMember`, `BrokerAccountServiceTests.GetMembersAsync_Throws_WhenCallerIsNotMember`, `BrokerAccountServiceTests.AddMemberAsync_AddsNewMember_WhenTargetExists`, `BrokerAccountServiceTests.AddMemberAsync_NormalizesEmailBeforeLookup`, `BrokerAccountServiceTests.AddMemberAsync_IsIdempotent_WhenTargetIsAlreadyMember`, `BrokerAccountServiceTests.AddMemberAsync_ThrowsKeyNotFound_WhenTargetEmailIsUnknown`, `BrokerAccountServiceTests.AddMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `BrokerAccountServiceTests.RemoveMemberAsync_RemovesMember_WhenMultipleRemain`, `BrokerAccountServiceTests.RemoveMemberAsync_ThrowsBadRequest_WhenRemovingLastMember`, `BrokerAccountServiceTests.RemoveMemberAsync_ThrowsKeyNotFound_WhenTargetIsNotMember`, `BrokerAccountServiceTests.RemoveMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember`, `BrokerAccountControllerTests.GetMembersAsync_Returns200WithMemberList`, `BrokerAccountControllerTests.AddMemberAsync_PassesEmailToService_AndReturnsMemberList`, `BrokerAccountControllerTests.RemoveMemberAsync_CallsServiceAndReturns204`

**Verdict: Matches.** Both ACs hold; the main-flow line "adds … by user id" is wrong in v1.0 itself — adding is by email, and was before v1.0 was written.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · A member adds a user → that user gains access to the broker and its stock transactions | Holds. A new `BrokerAccountMember` row is inserted. Broker reads and stock-transaction reads both filter on broker membership, so access is immediate. | `Fiolyn/Services/Internal/BrokerAccountService.cs:118-155`; `Fiolyn/Services/Internal/StockTransactionService.cs:89-90`, `:115-123` |
| AC2 · Non-member member-management → 404 | Holds. List, add and remove all throw `KeyNotFoundException` when the caller is not a member. | `Fiolyn/Services/Internal/BrokerAccountService.cs:100-106`, `:130-133`, `:165-168` |
| Main flow · "A member lists, adds and removes broker members by user id" | Differs: add is **by email** (`AddMemberRequest.Email`, normalized, unknown email → 404, idempotent if already a member). Remove is by user id. This is not drift: add-by-email dates from `c74a484` (2026-04-24), before v1.0. | `Fiolyn/Controllers/DTOs/Requests/Shared/AddMemberRequest.cs:9-13`, `Fiolyn/Controllers/BrokerAccountController.cs:118-134`, `Fiolyn/Services/Internal/BrokerAccountService.cs:120`, `:135-144` |
| Not in v1.0 · removing the last member | The service refuses with 400 ("delete the account instead"). A member may remove themselves when others remain. | `Fiolyn/Services/Internal/BrokerAccountService.cs:174-178` |

## US-14 — Administer any broker account (admin)

**Code:** `AdminBrokerAccountController` · `BrokerAccountService` · `IBrokerAccountService` · `AdminBrokerAccountCreateRequest` · `AdminBrokerAccountResponse` · `AuthConstants` · tests tagged `[Trait( "Story", "US-14" )]`

**Tests:** `BrokerAccountServiceTests.AdminUpdateAsync_ReturnsNull_WhenMissing`, `BrokerAccountServiceTests.AdminUpdateAsync_RenameOnly_SkipsSpendAccountLookup_AndSaves`, `BrokerAccountServiceTests.AdminUpdateAsync_PropagatesKeyNotFound_WhenSpendAccountMissing`, `BrokerAccountServiceTests.GetAllAsync_NoFilter_ReturnsAllAccountsOrderedByName`, `BrokerAccountServiceTests.GetAllAsync_WithUserIdFilter_ReturnsOnlyAccountsUserIsMemberOf`, `BrokerAccountServiceTests.GetByIdAsync_ReturnsAccount_RegardlessOfMembership`, `BrokerAccountServiceTests.GetByIdAsync_ReturnsNull_WhenNotFound`, `BrokerAccountServiceTests.AdminSoftDeleteAsync_SoftDeletes_WhenFound`, `BrokerAccountServiceTests.AdminSoftDeleteAsync_Propagates_KeyNotFoundException_FromRepository`, `AdminBrokerAccountControllerTests.GetAllAsync_NoFilter_ReturnsAdminResponseListWithMembers`, `AdminBrokerAccountControllerTests.GetAllAsync_WithUserIdFilter_PassesFilter`, `AdminBrokerAccountControllerTests.GetByIdAsync_Missing_Returns404`, `AdminBrokerAccountControllerTests.GetByIdAsync_Existing_Returns200WithAdminResponse`, `AdminBrokerAccountControllerTests.CreateAsync_UsesAppUserIdFromRequest_AndReturns201`, `AdminBrokerAccountControllerTests.UpdateAsync_Missing_Returns404`, `AdminBrokerAccountControllerTests.UpdateAsync_PassesScalarsToService_AndReturns200`, `AdminBrokerAccountControllerTests.DeleteAsync_Returns204_AndCallsUnscopedSoftDelete`

**Verdict: Matches.** All five admin endpoints sit behind the Admin policy and ignore the caller's membership. No test exercises the 403 path; it rests on the class-level attribute.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Non-admin → 403 | Holds (code only, no test). Class-level `[Authorize( Policy = AdminPolicyName )]`; the policy requires claim `isAdmin = "true"`. | `Fiolyn/Controllers/AdminBrokerAccountController.cs:17`, `Fiolyn/ServiceRegisters/AuthServiceRegister.cs:56-57`, `Fiolyn/Constants/AuthConstants.cs:11-17` |
| AC2 · Admin operates regardless of membership | Holds. Admin reads, update and delete use the unscoped service methods. Create does not check the admin's own membership, but it does require the named initial member (`appUserId`) to be a member of the linked spend account, or it returns 404. | `Fiolyn/Services/Internal/BrokerAccountService.cs:185-245`, `:24-26`; `Fiolyn/Controllers/AdminBrokerAccountController.cs:50-65` |
| Description · GET, GET /{id}, POST (`AdminBrokerAccountCreateRequest`), PUT /{id}, DELETE /{id} | Holds. GET also accepts an optional `?userId=` filter. An admin FK change only checks that the target spend account exists. | `Fiolyn/Controllers/AdminBrokerAccountController.cs:24-90`, `Fiolyn/Services/Internal/BrokerAccountService.cs:229-233` |

---
[← Module 3](module-3-spend-accounts.md) · [Index](README.md) · [Module 5 →](module-5-categories.md)
