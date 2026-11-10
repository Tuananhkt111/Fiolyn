# Module 5 — Transaction Categories

`TransactionCategory` is an admin-managed lookup bound to one `TransactionKind`, with a unique index on `(Kind, Name)`. Any authenticated user can list categories, optionally filtered by kind. Only admins can create, rename and delete them. No production code in this module has changed since v1.0 (2026-09-16).

**Verdict:** 2 of 2 stories match; no drift.

## US-15 — Browse categories

**Code:** `TransactionCategoryController` · `TransactionCategoryService` · `ITransactionCategoryService` · `TransactionCategory` · `TransactionKind` · `TransactionCategoryResponse` · tests tagged `[Trait( "Story", "US-15" )]`

**Tests:** `TransactionCategoryControllerTests.GetAllAsync_NoFilter_ReturnsAllCategories`, `TransactionCategoryControllerTests.GetAllAsync_WithKindFilter_PassesKindToService`, `TransactionCategoryControllerTests.GetAllAsync_EmptyList_ReturnsEmptyArray`, `TransactionCategoryServiceTests.GetAllAsync_FiltersByKind_WhenSpecified`, `AuthorizationTests.Enumeration_FindsEndpointsAndEveryAdminGroup`, `AuthorizationTests.Protected_WithoutToken_Returns401`, `AuthorizationTests.AppRoutes_MatchOpenApiOperations`

**Verdict: Matches.** One authenticated, read-only, paged list endpoint with an optional `?kind=` filter.

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Any authenticated user can list categories | Holds. The controller has a class-level `[Authorize]` with no policy and exposes only `GET`. | `Fiolyn/Controllers/TransactionCategoryController.cs:10-25` |
| AC2 · Filtering by kind returns only that kind's categories | Holds. | `Fiolyn/Services/Internal/TransactionCategoryService.cs:85-88` |
| Main flow · returns all categories as a `PagedResult` | Holds. It returns active categories only, because the soft-delete query filter hides deleted rows. Order is name ascending, then id. | `Fiolyn/Services/Internal/TransactionCategoryService.cs:81-93`, `Fiolyn/Data/AppDbContext.cs:127` |
| BR · read-only for non-admin users | Holds. Writes exist only on the admin controller. | `Fiolyn/Controllers/TransactionCategoryController.cs:19-25`, `docs/reference/openapi.json` (`/api/transaction-categories` has `get` only) |
| Exception · Unauthenticated → 401 | Holds. Produced by `[Authorize]` and JWT bearer auth. `AuthorizationTests` sends real requests through the auth pipeline (#23). | `Fiolyn/Controllers/TransactionCategoryController.cs:12` |

## US-16 — Manage categories (admin)

**Code:** `AdminTransactionCategoryController` · `TransactionCategoryService` · `TransactionCategory` · `TransactionCategoryCreateRequest` · `TransactionCategoryUpdateRequest` · `TransactionCategoryResponse` · `ConflictException` · tests tagged `[Trait( "Story", "US-16" )]`

**Tests:** `AdminTransactionCategoryControllerTests.CreateAsync_Returns201_WithBody`, `AdminTransactionCategoryControllerTests.GetByIdAsync_Returns404_WhenMissing`, `AdminTransactionCategoryControllerTests.GetAllAsync_PassesKindFilter`, `TransactionCategoryServiceTests.CreateAsync_ThrowsConflict_WhenSameKindNameExists`, `TransactionCategoryServiceTests.CreateAsync_AllowsSameNameDifferentKind`, `TransactionCategoryServiceTests.UpdateAsync_RenamesButKeepsKind`, `TransactionCategoryServiceTests.SoftDeleteAsync_ProcessesAndSaves`, `TransactionCategoryServiceTests.CreateAsync_NameOfADeletedCategory_RestoresItInsteadOfInserting`, `SoftDeleteRestoreTests.TransactionCategory_ReCreatingADeletedName_RestoresTheSameRow`, `AuthorizationTests.AdminOnly_NonAdminToken_Returns403`, `AuthorizationTests.AdminOnly_AdminToken_IsNeitherUnauthorizedNorForbidden`

**Verdict: Matches.** Admin-only CRUD; duplicates within a kind return 409; `Kind` cannot be changed. The 403 path is tested through the pipeline (#23).

| Criterion (v1.0) | Today | Evidence |
|---|---|---|
| AC1 · Non-admin write → 403 | Holds; `AuthorizationTests` (#23). Class-level Admin policy, which requires claim `isAdmin = "true"`. | `Fiolyn/Controllers/AdminTransactionCategoryController.cs:18`, `Fiolyn/ServiceRegisters/AuthServiceRegister.cs:56-57` |
| AC2 · Duplicate `(Kind, Name)` → 409; same name under a different kind is allowed | Holds. A pre-check throws `ConflictException`, and a Postgres unique violation on save is also mapped to `ConflictException`. The unique index is on `(Kind, Name)`. | `Fiolyn/Services/Internal/TransactionCategoryService.cs:25-30`, `:43-47`; `Fiolyn/Entities/TransactionCategory.cs:14`; `Fiolyn/Infrastructure/GlobalExceptionHandler.cs:24` |
| AC3 · Attempting to change a category's Kind is not honored | Holds. `TransactionCategoryUpdateRequest` has only `Name`, and the service sets only `Name`. | `Fiolyn/Controllers/DTOs/Requests/TransactionCategory/TransactionCategoryUpdateRequest.cs:9-10`, `Fiolyn/Services/Internal/TransactionCategoryService.cs:53-57` |
| Main flow · PUT /{id} updates the name; DELETE /{id} removes the category | Holds. DELETE is a soft delete. A rename that collides inside the kind also returns 409. | `Fiolyn/Services/Internal/TransactionCategoryService.cs:53-78`; `Fiolyn/Controllers/AdminTransactionCategoryController.cs:47-58` |
| Not in v1.0 · re-creating a deleted name | The unique index has no `IsDeleted` filter, so a soft-deleted category still holds its `(Kind, Name)`. Since #22 (ADR 0024), re-creating it restores that row instead of failing with 409. | `TransactionCategoryService.CreateAsync` |

---
[← Module 4](module-4-broker-accounts.md) · [Index](README.md) · [Module 6 →](module-6-transactions.md)
