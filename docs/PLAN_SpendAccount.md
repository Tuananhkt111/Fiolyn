# Plan: SpendAccount Entity + CRUD API

## Context

We manage **SpendAccount** records per `AppUser` to track balances across different places the user keeps money. Two account types are supported: **Cash** (wallet, bank account) and **Credit** (credit card). Each user owns multiple SpendAccounts.

Two controllers expose this resource:

- **User-scoped** (`/api/spend-accounts`) — a signed-in user only sees and modifies their own accounts.
- **Admin-scoped** (`/api/admin/spend-accounts`) — admins manage accounts across all users (list, get, create for any user, update, soft delete). Response DTO includes the owning user's email and display name.

Rows are user-owned, so the service layer filters by `AppUserId` for user-scoped calls (unlike `BrokerAccount` which is shared across users); admin-scoped calls eager-load `AppUser`.

**Money representation:** all monetary fields are `long` (whole-VND units). The existing `StockTransaction` entity previously used `decimal` for `Quantity` / `Price` / `TotalAmount` — this change also migrates those three fields to `long` so the whole project is consistent.

---

## 1. Entity Design

### `Fiolyn/Enums/SpendAccountType.cs`

```csharp
public enum SpendAccountType
{
  Cash,
  Credit
}
```

### `Fiolyn/Entities/SpendAccount.cs`

Implements `IAuditable` + `ISoftDelete`. Shape follows `StockTransaction` (the FK-plus-nav pattern with `AppUserId` / `AppUser`).

| Field          | Type                        | Notes                                                   |
|----------------|-----------------------------|---------------------------------------------------------|
| `Id`           | `long`                      | PK, auto-increment                                      |
| `AppUserId`    | `long` `[Required]`         | FK to `AppUser`                                         |
| `AppUser`      | `AppUser` nav               | `= null!`                                               |
| `Name`         | `string` `[MaxLength(100)]` | Display name, e.g. "VCB Savings"                        |
| `Type`         | `SpendAccountType` enum     | Cash / Credit                                           |
| `Balance`      | `long`                      | Current balance in whole VND (signed — may be negative) |
| `Created`      | `DateTime`                  | Auto-populated by `AppDbContext`                        |
| `LastModified` | `DateTime`                  | Auto-populated by `AppDbContext`                        |
| `IsDeleted`    | `bool`                      | Soft-delete flag                                        |

---

## 2. Data Layer

### `Fiolyn/Data/AppDbContext.cs`

Added `public DbSet<SpendAccount> SpendAccount => Set<SpendAccount>();` alongside existing DbSets (singular DbSet names per `CLAUDE.md`).

### Migration

EF scaffolds the full snapshot diff into one migration. Because both changes landed together, they ship as a single migration:

```bash
dotnet ef migrations add AddSpendAccountAndBigintAmounts --output-dir Data/Migrations
```

Contents:
- `CreateTable("SpendAccount", ...)` with FK to `AppUser` and `onDelete: Cascade`.
- `AlterColumn<long>` for `StockTransaction.Quantity` / `Price` / `TotalAmount` from `numeric` → `bigint`. EF prints *"An operation was scaffolded that may result in the loss of data"* — this is expected and is only relevant if existing rows have a fractional component. Review before applying.

Migrations are **not** applied on startup — apply manually via `dotnet ef database update` from `Fiolyn/` (per `CLAUDE.md` key rule).

---

## 3. Service Layer

### `Fiolyn/Services/Internal/Interfaces/ISpendAccountService.cs`

```csharp
public interface ISpendAccountService
{
  // Shared
  Task<SpendAccount> CreateAsync( SpendAccount account ) ;
  Task UpdateAsync( SpendAccount account ) ;

  // User-scoped
  Task<List<SpendAccount>> GetAllForUserAsync( long userId ) ;
  Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;

  // Admin
  Task<List<SpendAccount>> GetAllAsync( long? userId = null ) ;
  Task<SpendAccount?> GetByIdAsync( long id ) ;
  Task SoftDeleteAsync( long id ) ;
}
```

### `Fiolyn/Services/Internal/SpendAccountService.cs`

Injects `IRepository<SpendAccount>`. Ownership is **service-enforced**:

- `GetAllForUserAsync(userId)` — `.QueryActive().AsNoTracking().Where(a => a.AppUserId == userId).OrderBy(a => a.Name).ToListAsync()` — user-scoped list, no `AppUser` eager-load.
- `GetByIdForUserAsync(id, userId)` — `.QueryActive().FirstOrDefaultAsync(a => a.Id == id && a.AppUserId == userId)` (no `.AsNoTracking()` — callers mutate).
- `SoftDeleteAsync(id, userId)` — fetches via `GetByIdForUserAsync`, throws `KeyNotFoundException` (→ 404 via `GlobalExceptionHandler`) when the account is missing or not owned by the caller.
- `GetAllAsync(userId?)` — admin list, `.Include(a => a.AppUser)` for owner enrichment, optional `userId` filter composed into the query, ordered by `AppUserId` then `Name`.
- `GetByIdAsync(id)` — admin single-fetch with `.Include(a => a.AppUser)`, no user scoping.
- `SoftDeleteAsync(id)` — admin overload; uses `repository.GetActiveByIdOrThrowAsync(id)` and soft-deletes.
- `CreateAsync` / `UpdateAsync` — trust the controller to set `AppUserId` correctly from `User.GetUserId()` (user endpoints) or the admin request body (admin endpoints).

---

## 4. Controllers

### DTOs

`Fiolyn/Controllers/DTOs/Requests/SpendAccount/SpendAccountRequest.cs` — used by both user and admin `PUT`:

```csharp
public record SpendAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`Fiolyn/Controllers/DTOs/Requests/SpendAccount/AdminSpendAccountCreateRequest.cs` — admin `POST` (specifies target user):

```csharp
public record AdminSpendAccountCreateRequest(
  [Required] long AppUserId,
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`Fiolyn/Controllers/DTOs/Responses/SpendAccount/SpendAccountResponse.cs` — user-scoped response:

```csharp
public record SpendAccountResponse(
  long Id, long AppUserId, string Name, SpendAccountType Type, long Balance,
  DateTime Created, DateTime LastModified )
{
  public SpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.AppUserId, a.Name, a.Type, a.Balance, a.Created, a.LastModified ) { }
}
```

`Fiolyn/Controllers/DTOs/Responses/SpendAccount/AdminSpendAccountResponse.cs` — admin response, enriched with owner identity:

```csharp
public record AdminSpendAccountResponse(
  long Id, long AppUserId, string AppUserEmail, string? AppUserDisplayName,
  string Name, SpendAccountType Type, long Balance,
  DateTime Created, DateTime LastModified )
{
  public AdminSpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.AppUserId, a.AppUser.Email, a.AppUser.DisplayName,
            a.Name, a.Type, a.Balance, a.Created, a.LastModified ) { }
}
```

### `Fiolyn/Controllers/SpendAccountController.cs`

Route: `api/spend-accounts`. `[Authorize]` at class level (no Admin policy — every authenticated user manages their own accounts). `User.GetUserId()` (from `Fiolyn.Extensions.ClaimsPrincipalExtensions`) pipes the caller's ID into every service call.

| Verb     | Route          | Action                                      |
|----------|----------------|---------------------------------------------|
| `POST`   | `/`            | Create; sets `AppUserId = User.GetUserId()` |
| `GET`    | `/`            | List current user's accounts                |
| `GET`    | `/{id:long}`   | Get one (scoped to current user)            |
| `PUT`    | `/{id:long}`   | Full update (Name, Type, Balance)           |
| `DELETE` | `/{id:long}`   | Soft delete                                 |

No post-create refetch is needed (the response DTO doesn't read any navigation property; `AppUserId` is already on the entity).

### `Fiolyn/Controllers/AdminSpendAccountController.cs`

Route: `api/admin/spend-accounts`. `[Authorize(Policy = AuthConstants.AdminPolicyName)]` at class level.

| Verb     | Route        | Action                                                                                                                           |
|----------|--------------|----------------------------------------------------------------------------------------------------------------------------------|
| `GET`    | `/`          | List all accounts. Optional `?userId=X` filter. Always returns `AdminSpendAccountResponse[]` (owner email/displayName included). |
| `GET`    | `/{id:long}` | Get any account by ID (not scoped to caller).                                                                                    |
| `POST`   | `/`          | Create on behalf of `AppUserId` from the request body. Refetches via `GetByIdAsync` so the response includes the `AppUser` nav.  |
| `PUT`    | `/{id:long}` | Update Name / Type / Balance on any account. `AppUserId` is not re-assignable here.                                              |
| `DELETE` | `/{id:long}` | Soft-delete any account.                                                                                                         |

---

## 5. DI Registration

`Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs` — added:

```csharp
services.AddScoped<ISpendAccountService, SpendAccountService>() ;
```

---

## 6. Companion change: StockTransaction amounts → long

`Fiolyn/Entities/StockTransaction.cs` — `Quantity`, `Price`, `TotalAmount` changed from `decimal` to `long`. Matching changes:

- `StockTransactionRequest.cs` / `StockTransactionResponse.cs` — fields → `long`.
- `StockTransactionService.cs` `ParseBuySellAsync` / `ParseDividendAsync` — `decimal.TryParse` → `long.TryParse`.
- `StockTransactionServiceTests.cs` / `StockTransactionControllerTests.cs` — `100m` / `45000m` / `150.5m` literals → integer `L` literals (the sell-case test previously used `150.5`; dropped the fractional digit because whole-VND precision has no use for it).
- The combined EF migration contains `ALTER COLUMN ... TYPE bigint USING <col>::bigint` for each field.

---

## 7. Tests

- `Fiolyn.Tests/Unit/Services/SpendAccountServiceTests.cs` — user-scoped (`GetAllForUserAsync_FiltersByUserId`, `GetAllForUserAsync_OrdersByName`, `GetByIdForUserAsync_ReturnsAccount_WhenOwnedByUser`, `GetByIdForUserAsync_ReturnsNull_WhenOwnedByAnotherUser`, `CreateAsync_SavesViaRepository`, `UpdateAsync_UpdatesViaRepository`, `SoftDeleteAsync_SoftDeletes_WhenOwnedByUser`, `SoftDeleteAsync_ThrowsKeyNotFound_WhenAccountDoesNotBelongToUser`) plus admin (`GetAllAsync_NoFilter_ReturnsAllUsersAccountsOrderedByUserThenName`, `GetAllAsync_WithUserIdFilter_ReturnsOnlyThatUsersAccounts`, `GetByIdAsync_ReturnsAccount_RegardlessOfOwner`, `GetByIdAsync_ReturnsNull_WhenNotFound`, `AdminSoftDeleteAsync_SoftDeletes_WhenFound`, `AdminSoftDeleteAsync_Propagates_KeyNotFoundException_FromRepository`). Uses NSubstitute + `MockQueryable`.
- `Fiolyn.Tests/Unit/Controllers/SpendAccountControllerTests.cs` — `CreateAsync_SetsAppUserIdFromClaim_AndReturns201`, `GetAllAsync_PassesUserIdToService`, `GetByIdAsync_Missing_Returns404`, `GetByIdAsync_Existing_Returns200`, `UpdateAsync_Missing_Returns404`, `UpdateAsync_Existing_AppliesChangesAndReturns200`, `DeleteAsync_Returns204`. Injects a `sub` claim via `ControllerContext.HttpContext.User`, same pattern as `UserControllerTests`.
- `Fiolyn.Tests/Unit/Controllers/AdminSpendAccountControllerTests.cs` — `GetAllAsync_NoFilter_ReturnsAdminResponseList`, `GetAllAsync_WithUserIdFilter_PassesFilterAndReturnsAdminResponse`, `GetByIdAsync_Missing_Returns404`, `GetByIdAsync_Existing_Returns200WithAdminResponse`, `CreateAsync_UsesAppUserIdFromRequest_AndReturns201`, `UpdateAsync_Missing_Returns404`, `UpdateAsync_Existing_AppliesChangesAndReturns200`, `DeleteAsync_Returns204_AndCallsUnscopedSoftDelete`.

Full suite: **137 tests pass**.

---

## 8. Files Changed

| Action | File                                                                                  |
|--------|---------------------------------------------------------------------------------------|
| Create | `Fiolyn/Enums/SpendAccountType.cs`                                                    |
| Create | `Fiolyn/Entities/SpendAccount.cs`                                                     |
| Modify | `Fiolyn/Data/AppDbContext.cs`                                                         |
| Create | `Fiolyn/Services/Internal/Interfaces/ISpendAccountService.cs`                         |
| Create | `Fiolyn/Services/Internal/SpendAccountService.cs`                                     |
| Modify | `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs`                           |
| Create | `Fiolyn/Controllers/DTOs/Requests/SpendAccount/SpendAccountRequest.cs`                |
| Create | `Fiolyn/Controllers/DTOs/Requests/SpendAccount/AdminSpendAccountCreateRequest.cs`     |
| Create | `Fiolyn/Controllers/DTOs/Responses/SpendAccount/SpendAccountResponse.cs`              |
| Create | `Fiolyn/Controllers/DTOs/Responses/SpendAccount/AdminSpendAccountResponse.cs`         |
| Create | `Fiolyn/Controllers/SpendAccountController.cs`                                        |
| Create | `Fiolyn/Controllers/AdminSpendAccountController.cs`                                   |
| Create | `Fiolyn.Tests/Unit/Services/SpendAccountServiceTests.cs`                              |
| Create | `Fiolyn.Tests/Unit/Controllers/SpendAccountControllerTests.cs`                        |
| Create | `Fiolyn.Tests/Unit/Controllers/AdminSpendAccountControllerTests.cs`                   |
| Modify | `Fiolyn/Entities/StockTransaction.cs` (decimal → long)                                |
| Modify | `Fiolyn/Controllers/DTOs/Requests/StockTransaction/StockTransactionRequest.cs`        |
| Modify | `Fiolyn/Controllers/DTOs/Responses/StockTransaction/StockTransactionResponse.cs`      |
| Modify | `Fiolyn/Services/Internal/StockTransactionService.cs`                                 |
| Modify | `Fiolyn.Tests/Unit/Services/StockTransactionServiceTests.cs`                          |
| Modify | `Fiolyn.Tests/Unit/Controllers/StockTransactionControllerTests.cs`                    |
| Create | `Fiolyn/Data/Migrations/20260423023305_AddSpendAccountAndBigintAmounts.cs`            |

---

## 9. Verification

1. `dotnet build Fiolyn/Fiolyn.csproj` — ✓ 0 warnings, 0 errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — ✓ 137 tests pass.
3. Before deploy: run `dotnet ef database update` from `Fiolyn/` to apply `AddSpendAccountAndBigintAmounts`. Inspect the generated SQL if there are any existing `StockTransaction` rows — the `numeric` → `bigint` cast truncates fractional parts.
4. Manual smoke as a regular user (valid JWT required):
   - `POST /api/spend-accounts` with `{ "name": "Wallet", "type": 0, "balance": 1500000 }` → 201 + body.
   - `GET /api/spend-accounts` → returns only the caller's accounts.
   - As user B, `GET /api/spend-accounts/{userAId}` → 404 (ownership enforced in service).
   - `PUT /api/spend-accounts/{id}` with new balance → 200 with updated values.
   - `DELETE /api/spend-accounts/{id}` → 204; follow-up `GET` → 404.
5. Manual smoke as an admin (JWT with `isAdmin=true`):
   - `GET /api/admin/spend-accounts` → list of all users' accounts with owner email/displayName.
   - `GET /api/admin/spend-accounts?userId=X` → same shape, scoped to user X.
   - `POST /api/admin/spend-accounts` with `{ "appUserId": X, "name": "...", "type": 0, "balance": 0 }` → 201 + `AdminSpendAccountResponse`.
   - `PUT /api/admin/spend-accounts/{id}` / `DELETE /api/admin/spend-accounts/{id}` → 200 / 204.
   - As a non-admin, any `/api/admin/...` call → 403.

---

## Out of scope

- Credit-specific fields (credit limit, statement date, APR) — add when the UI needs them.
- Deposit / withdraw / transfer action endpoints — current `PUT` lets the client write `Balance` directly, sufficient for a first cut.
- Unique constraint on `(AppUserId, Name)` — skip until duplicates become a real UX problem.
