# Plan: SpendAccount Entity + CRUD API

## Context

We manage **SpendAccount** records per `AppUser` to track balances across different places the user keeps money. Two account types are supported: **Cash** (wallet, bank account) and **Credit** (credit card). Each user owns multiple SpendAccounts. No cross-user visibility: a signed-in user only sees and modifies their own accounts.

This follows the `BrokerAccount` + `StockTransaction` template, with one twist: rows are **user-owned**, so the service layer filters by `AppUserId` (unlike `BrokerAccount` which is shared across users).

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
  Task<SpendAccount> CreateAsync( SpendAccount account ) ;
  Task<List<SpendAccount>> GetAllForUserAsync( long userId ) ;
  Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) ;
  Task UpdateAsync( SpendAccount account ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
}
```

### `Fiolyn/Services/Internal/SpendAccountService.cs`

Injects `IRepository<SpendAccount>`. Ownership is **service-enforced**:

- `GetAllForUserAsync(userId)` — `.QueryActive().AsNoTracking().Where(a => a.AppUserId == userId).OrderBy(a => a.Name).ToListAsync()`
- `GetByIdForUserAsync(id, userId)` — `.QueryActive().FirstOrDefaultAsync(a => a.Id == id && a.AppUserId == userId)` (no `.AsNoTracking()` — callers mutate)
- `SoftDeleteAsync(id, userId)` — fetches via `GetByIdForUserAsync`, throws `KeyNotFoundException` (→ 404 via `GlobalExceptionHandler`) when the account is missing or not owned by the caller
- `CreateAsync` / `UpdateAsync` — trust the controller to set `AppUserId` correctly from `User.GetUserId()`

---

## 4. Controller

### DTOs

`Fiolyn/Controllers/DTOs/Requests/SpendAccount/SpendAccountRequest.cs`:

```csharp
public record SpendAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`Fiolyn/Controllers/DTOs/Responses/SpendAccount/SpendAccountResponse.cs`:

```csharp
public record SpendAccountResponse(
  long Id, long AppUserId, string Name, SpendAccountType Type, long Balance,
  DateTime Created, DateTime LastModified )
{
  public SpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.AppUserId, a.Name, a.Type, a.Balance, a.Created, a.LastModified ) { }
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

- `Fiolyn.Tests/Unit/Services/SpendAccountServiceTests.cs` — `GetAllForUserAsync_FiltersByUserId`, `GetAllForUserAsync_OrdersByName`, `GetByIdForUserAsync_ReturnsAccount_WhenOwnedByUser`, `GetByIdForUserAsync_ReturnsNull_WhenOwnedByAnotherUser`, `CreateAsync_SavesViaRepository`, `UpdateAsync_UpdatesViaRepository`, `SoftDeleteAsync_SoftDeletes_WhenOwnedByUser`, `SoftDeleteAsync_ThrowsKeyNotFound_WhenAccountDoesNotBelongToUser`. Uses NSubstitute + `MockQueryable`.
- `Fiolyn.Tests/Unit/Controllers/SpendAccountControllerTests.cs` — `CreateAsync_SetsAppUserIdFromClaim_AndReturns201`, `GetAllAsync_PassesUserIdToService`, `GetByIdAsync_Missing_Returns404`, `GetByIdAsync_Existing_Returns200`, `UpdateAsync_Missing_Returns404`, `UpdateAsync_Existing_AppliesChangesAndReturns200`, `DeleteAsync_Returns204`. Injects a `sub` claim via `ControllerContext.HttpContext.User`, same pattern as `UserControllerTests`.

Full suite: **123 tests pass**.

---

## 8. Files Changed

| Action | File                                                                             |
|--------|----------------------------------------------------------------------------------|
| Create | `Fiolyn/Enums/SpendAccountType.cs`                                               |
| Create | `Fiolyn/Entities/SpendAccount.cs`                                                |
| Modify | `Fiolyn/Data/AppDbContext.cs`                                                    |
| Create | `Fiolyn/Services/Internal/Interfaces/ISpendAccountService.cs`                    |
| Create | `Fiolyn/Services/Internal/SpendAccountService.cs`                                |
| Modify | `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs`                      |
| Create | `Fiolyn/Controllers/DTOs/Requests/SpendAccount/SpendAccountRequest.cs`           |
| Create | `Fiolyn/Controllers/DTOs/Responses/SpendAccount/SpendAccountResponse.cs`         |
| Create | `Fiolyn/Controllers/SpendAccountController.cs`                                   |
| Create | `Fiolyn.Tests/Unit/Services/SpendAccountServiceTests.cs`                         |
| Create | `Fiolyn.Tests/Unit/Controllers/SpendAccountControllerTests.cs`                   |
| Modify | `Fiolyn/Entities/StockTransaction.cs` (decimal → long)                           |
| Modify | `Fiolyn/Controllers/DTOs/Requests/StockTransaction/StockTransactionRequest.cs`   |
| Modify | `Fiolyn/Controllers/DTOs/Responses/StockTransaction/StockTransactionResponse.cs` |
| Modify | `Fiolyn/Services/Internal/StockTransactionService.cs`                            |
| Modify | `Fiolyn.Tests/Unit/Services/StockTransactionServiceTests.cs`                     |
| Modify | `Fiolyn.Tests/Unit/Controllers/StockTransactionControllerTests.cs`               |
| Create | `Fiolyn/Data/Migrations/20260423023305_AddSpendAccountAndBigintAmounts.cs`       |

---

## 9. Verification

1. `dotnet build Fiolyn/Fiolyn.csproj` — ✓ 0 warnings, 0 errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — ✓ 123 tests pass.
3. Before deploy: run `dotnet ef database update` from `Fiolyn/` to apply `AddSpendAccountAndBigintAmounts`. Inspect the generated SQL if there are any existing `StockTransaction` rows — the `numeric` → `bigint` cast truncates fractional parts.
4. Manual smoke (valid JWT required):
   - `POST /api/spend-accounts` with `{ "name": "Wallet", "type": 0, "balance": 1500000 }` → 201 + body.
   - `GET /api/spend-accounts` → returns only the caller's accounts.
   - As user B, `GET /api/spend-accounts/{userAId}` → 404 (ownership enforced in service).
   - `PUT /api/spend-accounts/{id}` with new balance → 200 with updated values.
   - `DELETE /api/spend-accounts/{id}` → 204; follow-up `GET` → 404.

---

## Out of scope

- Credit-specific fields (credit limit, statement date, APR) — add when the UI needs them.
- Deposit / withdraw / transfer action endpoints — current `PUT` lets the client write `Balance` directly, sufficient for a first cut.
- Unique constraint on `(AppUserId, Name)` — skip until duplicates become a real UX problem.
