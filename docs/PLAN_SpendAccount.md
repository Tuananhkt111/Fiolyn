# Plan: SpendAccount Entity + Shared CRUD API

## Context

**SpendAccount** records track balances across different places the user keeps money. Two account types are supported: **Cash** (wallet, bank account) and **Credit** (credit card). Each account has one or more **members** (users who share the account) via the `SpendAccountMember` join entity — a household couple, for example, both hold membership on the same `VCB Savings` row.

Two controllers expose this resource:

- **User-scoped** (`/api/spend-accounts`) — the signed-in caller sees and manages accounts they are a member of. Includes endpoints for managing the membership list.
- **Admin-scoped** (`/api/admin/spend-accounts`) — admins manage accounts across all users (list, get, create on behalf of any user, update, soft-delete). Response DTO includes the full member list (email + display name).

Access is controlled by membership: the service layer filters `SpendAccount` by `Members.Any(m => m.AppUserId == callerId && !m.AppUser.IsDeleted)` for user-scoped calls. "Not a member" is surfaced as 404 via `KeyNotFoundException`. All members are equal — there is no owner vs editor vs viewer role split.

**Money representation:** all monetary fields are `long` (whole-VND units). This matches the project-wide rule documented in `.claude/CLAUDE.md`.

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

Implements `IAuditable` + `ISoftDelete`. No direct FK to `AppUser` — access is via the `Members` collection.

| Field          | Type                              | Notes                                                   |
|----------------|-----------------------------------|---------------------------------------------------------|
| `Id`           | `long`                            | PK, auto-increment                                      |
| `Name`         | `string` `[MaxLength(100)]`       | Display name, e.g. "VCB Savings"                        |
| `Type`         | `SpendAccountType` enum           | Cash / Credit                                           |
| `Balance`      | `long`                            | Current balance in whole VND (signed — may be negative) |
| `Members`      | `ICollection<SpendAccountMember>` | Membership set (access-control)                         |
| `Created`      | `DateTime`                        | Auto-populated by `AppDbContext`                        |
| `LastModified` | `DateTime`                        | Auto-populated by `AppDbContext`                        |
| `IsDeleted`    | `bool`                            | Soft-delete flag                                        |

### `Fiolyn/Entities/SpendAccountMember.cs`

Explicit join entity. Composite PK + reverse index via data annotations (no `OnModelCreating` override needed):

```csharp
[PrimaryKey( nameof( SpendAccountId ), nameof( AppUserId ) )]
[Index( nameof( AppUserId ), nameof( SpendAccountId ) )]
public class SpendAccountMember
{
  public long SpendAccountId { get ; set ; }
  public SpendAccount SpendAccount { get ; set ; } = null! ;

  public long AppUserId { get ; set ; }
  public AppUser AppUser { get ; set ; } = null! ;

  public DateTime JoinedAt { get ; set ; }
}
```

**Design choices:**

- **No `IAuditable`/`ISoftDelete`.** A membership row is a presence marker — join = insert, leave = hard delete. There is no "modified" state to audit and no "deleted but restorable" state. A single `JoinedAt` timestamp (set manually in `AddMemberAsync`) is enough.
- **Composite PK on `(SpendAccountId, AppUserId)`** prevents duplicate memberships at the DB layer. The reverse index `(AppUserId, SpendAccountId)` serves the hot path `GetAllForUserAsync` — Postgres cannot efficiently answer "which accounts is user X in?" using only the leftmost-on-SpendAccountId PK index.
- **Cascade delete on both FKs.** Hard-deleting a user or an account cleans up membership rows. Soft-deleting the parent `SpendAccount` is handled by `QueryActive()` filtering — no need to soft-delete the join rows themselves.

### `Fiolyn/Entities/AppUser.cs`

Adds a collection nav for symmetry with `RefreshTokens`:

```csharp
public ICollection<SpendAccountMember> SpendAccountMemberships { get ; set ; } = [] ;
```

---

## 2. Data Layer

### `Fiolyn/Data/AppDbContext.cs`

```csharp
public DbSet<SpendAccount> SpendAccount => Set<SpendAccount>() ;
public DbSet<SpendAccountMember> SpendAccountMember => Set<SpendAccountMember>() ;
```

No `OnModelCreating` override — all relational rules are expressed via data annotations.

### Migrations

Two migrations exist for this feature:

1. `20260423023305_AddSpendAccountAndBigintAmounts` — initial single-owner `SpendAccount` table + `StockTransaction` money fields migrated to `bigint`.
2. `20260423090218_SpendAccountMultiMember` — **current** — introduces `SpendAccountMember`, backfills from `SpendAccount.AppUserId`, and drops the column.

The multi-member migration is hand-edited so the steps run in this order:

1. `CreateTable("SpendAccountMember")` with composite PK, FKs, reverse index.
2. Raw-SQL backfill: `INSERT INTO "SpendAccountMember" ("SpendAccountId", "AppUserId", "JoinedAt") SELECT "Id", "AppUserId", "Created" FROM "SpendAccount"` — each existing single-owner row becomes a one-member account, preserving the original creation time as `JoinedAt`.
3. Drop FK, index, and column `AppUserId` from `SpendAccount`.

The `Down` reverses the sequence (adds `AppUserId` back, backfills one member per account via `SELECT DISTINCT ON (SpendAccountId)` ordered by `JoinedAt`, restores FK + index, drops the join table). Down is lossy when an account has more than one member.

Migrations are **not** applied on startup — apply manually via `dotnet ef database update` from `Fiolyn/` (per `CLAUDE.md` key rule).

**Live-deploy note.** Because migrations run before the new code image boots (Northflank release step or manual `dotnet ef database update`), there is a brief window after the migration where the old running app reads `SpendAccount.AppUserId` — which has been dropped — and 500s every SpendAccount query. The simplest fix for this small/solo project is a brief maintenance window: stop the app, run the migration, deploy the new image. A two-phase split (migration A creates join + backfill while old code dual-writes; migration B drops the column after the new code is live) is documented as an option but is more ceremony than this project needs.

---

## 3. Service Layer

### `Fiolyn/Services/Internal/Interfaces/ISpendAccountService.cs`

```csharp
public interface ISpendAccountService
{
  // User-scoped (membership = access)
  Task<SpendAccount> CreateAsync( SpendAccount account, long creatorUserId ) ;
  Task<List<SpendAccount>> GetAllForUserAsync( long userId ) ;
  Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) ;
  Task UpdateAsync( SpendAccount account ) ;
  Task SoftDeleteAsync( long id, long userId ) ;

  // Member management (caller must be a member)
  Task<List<SpendAccountMember>> GetMembersAsync( long accountId, long callerUserId ) ;
  Task<List<SpendAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email ) ;
  Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId ) ;

  // Admin (unscoped)
  Task<List<SpendAccount>> GetAllAsync( long? userId = null ) ;
  Task<SpendAccount?> GetByIdAsync( long id ) ;
  Task SoftDeleteAsync( long id ) ;
}
```

### `Fiolyn/Services/Internal/SpendAccountService.cs`

Injects `IRepository<SpendAccount>`, `IRepository<SpendAccountMember>`, and `IRepository<AppUser>` via primary constructor.

Key behaviors:

- **`CreateAsync(account, creatorUserId)`** — adds a `SpendAccountMember { AppUserId = creatorUserId, JoinedAt = UtcNow }` to `account.Members` **before** `AddAsync` + `SaveAsync`. Setting `AppUserId` scalar is safe (user already exists); EF fills in `SpendAccountId` after the parent insert within the same transaction (per the CLAUDE.md rule: set the nav, not the scalar FK, for parents staged in the same transaction).
- **`GetAllForUserAsync(userId)`** — `.QueryActive().AsNoTracking().Where(a => a.Members.Any(m => m.AppUserId == userId && !m.AppUser.IsDeleted)).OrderBy(a => a.Name).ToListAsync()`.
- **`GetByIdForUserAsync(id, userId)`** — same filter + `.Id == id`. No `.AsNoTracking()` (callers mutate).
- **`SoftDeleteAsync(id, userId)`** — fetches via `GetByIdForUserAsync`, throws `KeyNotFoundException` (→ 404) when the account is missing or caller is not a member. `ISoftDelete` on `SpendAccount` means the join rows stay intact — `QueryActive` filters the parent, so they're effectively hidden.
- **`GetMembersAsync` / `AddMemberAsync` / `RemoveMemberAsync`** — all verify caller membership up front via a private `EnsureCallerIsMemberAsync` helper. Target lookup for `AddMemberAsync` uses `AppUser.NormalizeEmail(email)` + `IRepository<AppUser>.QueryActive()`; unknown emails → 404. Idempotent: adding an already-joined email is a no-op + 200 with the current list. `RemoveMemberAsync` counts remaining members and throws `BadRequestException` (→ 400) if the removal would leave the account empty — the caller is told to delete the account instead.
- **`GetAllAsync(userId?)`** — admin list. `.Include(a => a.Members).ThenInclude(m => m.AppUser)` for response enrichment. Optional `userId` filter: `a.Members.Any(m => m.AppUserId == userId.Value)`. Ordered by `Name`.
- **`GetByIdAsync(id)`** — admin single-fetch with the same includes, unscoped.
- **`SoftDeleteAsync(id)`** — admin overload; uses `repository.GetActiveByIdOrThrowAsync(id)` and soft-deletes.

Admin member-management endpoints are intentionally **not** added. An admin who needs to manage members creates an account for themselves (admin POST) or adds themselves as a member through the user endpoint, then acts normally. Keeps the API surface small and auditable.

### New exception type

`Fiolyn/Infrastructure/Exceptions/BadRequestException.cs` + a branch in `GlobalExceptionHandler` mapping it → 400. Mirrors `ForbiddenAccessException`.

---

## 4. Controllers

### DTOs

`SpendAccountRequest` (user + admin `PUT` / user `POST`):

```csharp
public record SpendAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`AdminSpendAccountCreateRequest` (admin `POST` — specifies the initial member):

```csharp
public record AdminSpendAccountCreateRequest(
  [Required] long AppUserId,
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`AddMemberRequest` (member add endpoint):

```csharp
public record AddMemberRequest(
  [Required] [EmailAddress] [MaxLength( 256 )] string Email ) ;
```

`SpendAccountResponse` — **drops the legacy `AppUserId` field**. The caller's identity is carried on the JWT and does not need to be echoed in the payload; callers are always a member when this record is returned from a user-scoped endpoint:

```csharp
public record SpendAccountResponse(
  long Id, string Name, SpendAccountType Type, long Balance,
  DateTime Created, DateTime LastModified )
{
  public SpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.Name, a.Type, a.Balance, a.Created, a.LastModified ) { }
}
```

`AdminSpendAccountResponse` — drops the single-owner fields in favor of a member list:

```csharp
public record AdminSpendAccountResponse(
  long Id, string Name, SpendAccountType Type, long Balance,
  IReadOnlyList<SpendAccountMemberResponse> Members,
  DateTime Created, DateTime LastModified ) ;
```

`SpendAccountMemberResponse`:

```csharp
public record SpendAccountMemberResponse(
  long AppUserId, string Email, string? DisplayName, DateTime JoinedAt ) ;
```

### `Fiolyn/Controllers/SpendAccountController.cs`

Route: `api/spend-accounts`. `[Authorize]`. Pipes `User.GetUserId()` into every service call.

| Verb     | Route                         | Action                                                                    |
|----------|-------------------------------|---------------------------------------------------------------------------|
| `POST`   | `/`                           | Create; caller becomes the first member.                                  |
| `GET`    | `/`                           | List accounts caller is a member of.                                      |
| `GET`    | `/{id:long}`                  | Get one. 404 if caller is not a member.                                   |
| `PUT`    | `/{id:long}`                  | Update Name / Type / Balance. 404 if caller is not a member.              |
| `DELETE` | `/{id:long}`                  | Soft-delete. 404 if caller is not a member.                               |
| `GET`    | `/{id:long}/members`          | List current members. 404 if caller is not a member.                      |
| `POST`   | `/{id:long}/members`          | Add a member by email. Idempotent — 200 + list if target is already in.   |
| `DELETE` | `/{id:long}/members/{userId}` | Remove a member. 400 if it would leave the account empty.                 |

### `Fiolyn/Controllers/AdminSpendAccountController.cs`

Route: `api/admin/spend-accounts`. `[Authorize(Policy = AuthConstants.AdminPolicyName)]`.

- `GET /` — list all accounts; optional `?userId=X` narrows to accounts X is a member of. Returns with full `Members` list.
- `GET /{id:long}` — get any account by ID, not scoped.
- `POST /` — create on behalf of the specified `AppUserId` (becomes the initial member). Response includes the member list.
- `PUT /{id:long}` — update Name / Type / Balance on any account.
- `DELETE /{id:long}` — soft-delete any account.

---

## 5. DI Registration

`Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs`:

```csharp
services.AddScoped<ISpendAccountService, SpendAccountService>() ;
```

`IRepository<SpendAccount>`, `IRepository<SpendAccountMember>`, and `IRepository<AppUser>` all resolve through the existing generic repository registration.

---

## 6. Tests

- `Fiolyn.Tests/Unit/Services/SpendAccountServiceTests.cs` — covers the full service surface: create + creator-as-first-member, membership filtering, soft-deleted member filtering, ordering, update, user-scoped soft-delete (+ second-call-returns-404 idempotency), member list / add (+ email normalization, + idempotent duplicate, + unknown email, + non-member caller), member remove (+ last-member → `BadRequestException`, + non-member target, + non-member caller), admin list / get / soft-delete.
- `Fiolyn.Tests/Unit/Controllers/SpendAccountControllerTests.cs` — covers caller-id plumbing, 404 vs 200 paths on all CRUD, and the new member endpoints (`GET /members`, `POST /members`, `DELETE /members/{userId}`).
- `Fiolyn.Tests/Unit/Controllers/AdminSpendAccountControllerTests.cs` — updated response-shape expectations for the member list and admin create's `(account, initialMemberUserId)` service call.

Full suite: **154 tests pass**.

---

## 7. Verification

1. `dotnet build Fiolyn.sln` — 0 warnings, 0 errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — 154 pass.
3. Before deploy: run `dotnet ef database update` from `Fiolyn/` to apply `SpendAccountMultiMember`. Take a brief maintenance window or dual-deploy as described in section 2.
4. Manual smoke as a regular user (valid JWT required):
   - User A `POST /api/spend-accounts` with `{ "name": "Wallet", "type": 0, "balance": 1500000 }` → 201 + body.
   - User A `POST /api/spend-accounts/{id}/members` with `{ "email": "<userB@...>" }` → 200 + member list containing A and B.
   - User B `GET /api/spend-accounts` → sees the shared account.
   - User B `PUT /api/spend-accounts/{id}` updates balance → 200.
   - User B `DELETE /api/spend-accounts/{id}/members/{A.id}` → 204; User A `GET /api/spend-accounts/{id}` → 404.
   - User B `DELETE /api/spend-accounts/{id}/members/{B.id}` (last member) → 400 with "Bad Request".
   - User B `DELETE /api/spend-accounts/{id}` → 204.
5. Manual smoke as an admin:
   - `GET /api/admin/spend-accounts` → list with full `Members` arrays.
   - `GET /api/admin/spend-accounts?userId=X` → scoped to accounts X is in.
   - `POST /api/admin/spend-accounts` with `{ "appUserId": X, "name": "...", "type": 0, "balance": 0 }` → 201 + `AdminSpendAccountResponse` with X as sole member.
   - `DELETE /api/admin/spend-accounts/{id}` → 204.
   - As a non-admin, any `/api/admin/...` call → 403.

---

## Out of scope

- Credit-specific fields (credit limit, statement date, APR).
- Deposit / withdraw / transfer action endpoints — the current `PUT` lets the client write `Balance` directly.
- Roles (owner / editor / viewer) — all members are equal for now. Add a `role` column on `SpendAccountMember` if a real access-tier need appears.
- Invite + accept flow — the direct add-by-email is simple and sufficient. Revisit if users start sharing accounts with non-registered emails.
- Unique constraint on `(<any-member>, Name)` — duplicate names across the shared set are tolerated.
