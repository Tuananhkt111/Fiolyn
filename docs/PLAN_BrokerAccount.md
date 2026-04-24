# Plan: BrokerAccount Multi-Member + StockTransaction Membership-Based Access

## Context

**BrokerAccount** records represent trading / brokerage accounts (e.g. VPS, TCBS) used as the parent for
`StockTransaction` rows. Previously `BrokerAccount` had no `AppUserId` column — all authenticated users could CRUD every
broker account, and `StockTransaction` inherited the same lack of scoping through its `BrokerAccountId` FK.

This refactor shares broker accounts across one or more **members** (users who share the account) via the
`BrokerAccountMember` join entity, and threads access control through to `StockTransaction`: any member of a broker can
CRUD every transaction on that broker; non-members see 404.

Two controllers expose each resource:

- **User-scoped** `/api/broker-accounts`, `/api/stock-transactions` — the signed-in caller sees / manages accounts they
  are a member of, and the transactions on those accounts. Broker endpoints include membership management.
- **Admin-scoped** `/api/admin/broker-accounts`, `/api/admin/stock-transactions` — admins act across all users (no
  membership check). `AdminBrokerAccountResponse` carries the full member list.

The **Telegram ingest path** keeps its existing semantics — it resolves brokers by name globally (no caller id) and
writes transactions via a new `CreateFromTelegramAsync` method that deliberately skips the membership check.
`TelegramUserInfo` is unchanged.

All members are equal — there is no owner / editor / viewer split.

---

## 1. Entity Design

### `Fiolyn/Entities/BrokerAccountMember.cs`

Explicit join entity. Composite PK + reverse index via data annotations — no `OnModelCreating` override:

```csharp
[PrimaryKey( nameof( BrokerAccountId ), nameof( AppUserId ) )]
[Index( nameof( AppUserId ), nameof( BrokerAccountId ) )]
public class BrokerAccountMember
{
  public long BrokerAccountId { get ; set ; }
  public BrokerAccount BrokerAccount { get ; set ; } = null! ;

  public long AppUserId { get ; set ; }
  public AppUser AppUser { get ; set ; } = null! ;

  public DateTime JoinedAt { get ; set ; }
}
```

**Design choices** (mirror `SpendAccountMember`):

- **No `IAuditable`/`ISoftDelete`.** Membership is presence-only — join = insert, leave = hard delete.
- **Composite PK on `(BrokerAccountId, AppUserId)`** prevents duplicate memberships. Reverse index
  `(AppUserId, BrokerAccountId)` serves the hot path `GetAllForUserAsync`.
- **Cascade delete** on both FKs — hard-deleting a user or broker cleans up memberships. Soft-deleting the parent
  `BrokerAccount` is handled by `QueryActive()` filtering.

### `Fiolyn/Entities/BrokerAccount.cs`

Existing fields unchanged (no `AppUserId` column to drop). Adds:

```csharp
public ICollection<BrokerAccountMember> Members { get ; set ; } = [] ;
```

### `Fiolyn/Entities/StockTransaction.cs`

**No schema change.** Access flows transitively via `BrokerAccount.Members`. `BrokerAccountId` stays as the only scoping
edge.

### `Fiolyn/Entities/AppUser.cs`

Adds a collection nav for symmetry:

```csharp
public ICollection<BrokerAccountMember> BrokerAccountMemberships { get ; set ; } = [] ;
```

---

## 2. Data Layer

### `Fiolyn/Data/AppDbContext.cs`

```csharp
public DbSet<BrokerAccount> BrokerAccount => Set<BrokerAccount>() ;
public DbSet<BrokerAccountMember> BrokerAccountMember => Set<BrokerAccountMember>() ;
```

No `OnModelCreating` override.

### Migration

`20260424135910_BrokerAccountMultiMember` — hand-edited so `Up()` runs in this order:

1. `CreateTable("BrokerAccountMember")` with composite PK, cascade-delete FKs on both sides.
2. `CreateIndex` for the reverse `(AppUserId, BrokerAccountId)` lookup.
3. **Backfill (raw SQL):** pick the lowest-`Id` active admin, fail loudly if none exists, assign every active
   `BrokerAccount` to that admin as sole member:

   ```sql
   DO $$
   DECLARE admin_id bigint ;
   BEGIN
     SELECT "Id" INTO admin_id
     FROM "AppUser"
     WHERE "IsAdmin" = TRUE AND "IsDeleted" = FALSE
     ORDER BY "Id"
     LIMIT 1 ;

     IF admin_id IS NULL THEN
       RAISE EXCEPTION 'BrokerAccountMultiMember migration requires at least one active admin AppUser.' ;
     END IF ;

     INSERT INTO "BrokerAccountMember" ("BrokerAccountId", "AppUserId", "JoinedAt")
     SELECT "Id", admin_id, "Created"
     FROM "BrokerAccount"
     WHERE "IsDeleted" = FALSE ;
   END $$ ;
   ```

`Down()` simply drops the table — `BrokerAccount` never had an `AppUserId` column, so there's nothing to restore.

**Migration precondition:** at least one active admin must exist in `AppUser`. The migration raises an exception
otherwise, rather than silently orphaning every broker (which would lock every user out of every account until a manual
fix).

Migrations are **not** applied on startup — apply manually via `dotnet ef database update` from `Fiolyn/` (per
`CLAUDE.md`).

### Live-deploy note

Unlike the `SpendAccount` multi-member refactor (which dropped a column), this migration is **strictly additive** on the
schema — it only creates a new table + index and inserts rows. The old running app (which has no membership check)
continues to work correctly between `dotnet ef database update` and the new-code deploy.

The only behavior change lands when the new code boots: every **non-admin** user momentarily sees zero brokers until
they're re-added to the relevant broker accounts by the admin. This is expected given the "first admin only" backfill
decision — the trade-off was taken deliberately to avoid silently sharing every broker with every user.

---

## 3. Service Layer

### `Fiolyn/Services/Internal/Interfaces/IBrokerAccountService.cs`

```csharp
public interface IBrokerAccountService
{
  // User-scoped (membership = access)
  Task<BrokerAccount> CreateAsync( BrokerAccount account, long creatorUserId ) ;
  Task<List<BrokerAccount>> GetAllForUserAsync( long userId ) ;
  Task<BrokerAccount?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task UpdateAsync( BrokerAccount account ) ;
  Task SoftDeleteAsync( long id, long userId ) ;

  // Member management (caller must be a member)
  Task<List<BrokerAccountMember>> GetMembersAsync( long accountId, long callerUserId ) ;
  Task<List<BrokerAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email ) ;
  Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId ) ;

  // Admin (unscoped)
  Task<List<BrokerAccount>> GetAllAsync( long? userId = null ) ;
  Task<BrokerAccount?> GetByIdAsync( long id ) ;
  Task SoftDeleteAsync( long id ) ;
}
```

### `Fiolyn/Services/Internal/BrokerAccountService.cs`

Injects `IRepository<BrokerAccount>`, `IRepository<BrokerAccountMember>`, and `IRepository<AppUser>` via primary
constructor. Behaviors mirror `SpendAccountService`:

- **`CreateAsync(account, creatorUserId)`** — adds a member row to `account.Members` *before* `AddAsync` + `SaveAsync`.
  Sets the nav (not the scalar `BrokerAccountId`) so EF fills in the parent ID after the insert.
- **`GetAllForUserAsync` / `GetByIdForUserAsync` / `ExistsForUserAsync`** — filter via
  `.Where(b => b.Members.Any(m => m.AppUserId == userId && !m.AppUser.IsDeleted))`.
- **`SoftDeleteAsync(id, userId)`** — fetch via `GetByIdForUserAsync`; throw `KeyNotFoundException` if null. The join
  rows stay intact; `QueryActive` filters the parent.
- **`GetMembersAsync` / `AddMemberAsync` / `RemoveMemberAsync`** — all verify caller membership via
  `EnsureCallerIsMemberAsync`. Add is idempotent on duplicate + 404 on unknown email. Remove counts remaining members
  and throws `BadRequestException` (→ 400) if removal would leave the account empty.
- **Admin overloads** — `GetAllAsync(userId?)`, `GetByIdAsync(id)`, `SoftDeleteAsync(long)` with
  `.Include(b => b.Members).ThenInclude(m => m.AppUser)` for response enrichment.

Admin member-management endpoints are intentionally **not** added — an admin who needs to manage members just joins the
broker themselves (admin POST with their own `AppUserId`) or edits through the user endpoint.

### `Fiolyn/Services/Internal/Interfaces/IStockTransactionService.cs`

```csharp
public interface IStockTransactionService
{
  Task<StockTransaction?> ParseMessageAsync( string text ) ;

  // User-scoped
  Task<StockTransaction> CreateAsync( StockTransaction transaction, long userId ) ;
  Task<List<StockTransaction>> GetAllForUserAsync( long userId, string? symbol = null ) ;
  Task<StockTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task UpdateAsync( StockTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;

  // Trusted-internal (Telegram ingest — bypasses membership)
  Task<StockTransaction> CreateFromTelegramAsync( StockTransaction transaction ) ;

  // Admin (unscoped)
  Task<List<StockTransaction>> GetAllAsync( long? userId = null, string? symbol = null ) ;
  Task<StockTransaction?> GetByIdAsync( long id ) ;
  Task SoftDeleteAsync( long id ) ;
}
```

### `Fiolyn/Services/Internal/StockTransactionService.cs`

Access check via private `EnsureUserIsBrokerMemberAsync(brokerId, userId)` — scoped reads filter
`t.BrokerAccount.Members.Any(m => m.AppUserId == userId && !m.AppUser.IsDeleted)`. The helper lives in
`StockTransactionService` (not `BrokerAccountService`) to avoid a DI cycle between the two services.

- **`CreateAsync(tx, userId)`** — membership check on `tx.BrokerAccountId` up front, then save.
- **`UpdateAsync(tx, userId)`** — membership check against the current `BrokerAccountId` on the staged entity (covers
  broker reassignment — user must be a member of the new broker too).
- **`CreateFromTelegramAsync(tx)`** — no membership check. Documented in the interface XML comment as "for trusted
  internal callers only" — used exclusively by `TelegramMessageService`.

### `Fiolyn/Services/External/Telegram/TelegramMessageService.cs`

Single-line change: the stock ingest path now calls `stockTransactionService.CreateFromTelegramAsync(transaction)`
instead of `CreateAsync(transaction)`. Everything else (sender allowlist check, broker resolution, Notion fallback) is
untouched.

### Infrastructure

Reuses existing `BadRequestException` + `GlobalExceptionHandler` mapping (→ 400) for the last-member-removal guard. No
new exception types or handler branches.

---

## 4. Controllers

### Broker DTOs

`BrokerAccountRequest` (user + admin `PUT` / user `POST`) — unchanged:

```csharp
public record BrokerAccountRequest(
  [Required] [MaxLength( 100 )] string Name ) ;
```

`AdminBrokerAccountCreateRequest` (admin `POST` — specifies the initial member):

```csharp
public record AdminBrokerAccountCreateRequest(
  [Required] long AppUserId,
  [Required] [MaxLength( 100 )] string Name ) ;
```

`AddMemberRequest`:

```csharp
public record AddMemberRequest(
  [Required] [EmailAddress] [MaxLength( 256 )] string Email ) ;
```

`BrokerAccountResponse` — unchanged shape. Unlike `SpendAccountResponse`, it has no legacy `AppUserId` field to
preserve.

`AdminBrokerAccountResponse`:

```csharp
public record AdminBrokerAccountResponse(
  long Id, string Name,
  IReadOnlyList<BrokerAccountMemberResponse> Members,
  DateTime Created, DateTime LastModified ) ;
```

`BrokerAccountMemberResponse`:

```csharp
public record BrokerAccountMemberResponse(
  long AppUserId, string Email, string? DisplayName, DateTime JoinedAt ) ;
```

### Transaction DTOs

`StockTransactionRequest` and `StockTransactionResponse` — **unchanged**. They already carry `BrokerAccountId` (and
response includes `BrokerAccountName`).

### `Fiolyn/Controllers/BrokerAccountController.cs`

Route: `api/broker-accounts`. `[Authorize]`. Pipes `User.GetUserId()` into every service call.

| Verb     | Route                         | Action                                                                  |
|----------|-------------------------------|-------------------------------------------------------------------------|
| `POST`   | `/`                           | Create; caller becomes the first member.                                |
| `GET`    | `/`                           | List accounts caller is a member of.                                    |
| `GET`    | `/{id:long}`                  | Get one. 404 if caller is not a member.                                 |
| `HEAD`   | `/{id:long}`                  | Existence probe — also scoped to caller's membership.                   |
| `PUT`    | `/{id:long}`                  | Update Name. 404 if caller is not a member.                             |
| `DELETE` | `/{id:long}`                  | Soft-delete. 404 if caller is not a member.                             |
| `GET`    | `/{id:long}/members`          | List current members. 404 if caller is not a member.                    |
| `POST`   | `/{id:long}/members`          | Add a member by email. Idempotent — 200 + list if target is already in. |
| `DELETE` | `/{id:long}/members/{userId}` | Remove a member. 400 if it would leave the account empty.               |

### `Fiolyn/Controllers/AdminBrokerAccountController.cs`

Route: `api/admin/broker-accounts`. `[Authorize(Policy = AuthConstants.AdminPolicyName)]`.

- `GET /` — list all broker accounts (optional `?userId=X` scopes to X's memberships). Returns full `Members` list.
- `GET /{id:long}` — get any account by ID.
- `POST /` — create on behalf of `AppUserId` (becomes the initial member). Response refetches to include member nav.
- `PUT /{id:long}` — update Name on any account.
- `DELETE /{id:long}` — soft-delete any account.

### `Fiolyn/Controllers/StockTransactionController.cs`

Route: `api/stock-transactions`. `[Authorize]`. Pipes `User.GetUserId()` into every service call.

| Verb     | Route        | Action                                                                        |
|----------|--------------|-------------------------------------------------------------------------------|
| `POST`   | `/`          | Create transaction; 404 if caller is not a member of the target broker.       |
| `GET`    | `/`          | List transactions on brokers caller is a member of. `?symbol=` filter.        |
| `GET`    | `/{id:long}` | Get one; 404 if caller is not a member of its broker.                         |
| `HEAD`   | `/{id:long}` | Existence probe — scoped to caller's membership of the broker.                |
| `PUT`    | `/{id:long}` | Update; 404 on current broker or reassigned broker if caller is not a member. |
| `DELETE` | `/{id:long}` | Soft-delete; 404 if caller is not a member of the broker.                     |

### `Fiolyn/Controllers/AdminStockTransactionController.cs`

Route: `api/admin/stock-transactions`. `[Authorize(Policy = AuthConstants.AdminPolicyName)]`.

- `GET /` — list all; optional `?userId=X` scopes to transactions on X's brokers, `?symbol=` filter.
- `GET /{id:long}` — get any transaction by ID.
- `DELETE /{id:long}` — soft-delete any transaction.

No admin `POST` / `PUT` — cross-user create isn't a requested workflow, and any admin who needs to write a transaction
just joins the broker first.

---

## 5. DI Registration

`IRepository<BrokerAccount>`, `IRepository<BrokerAccountMember>`, `IRepository<AppUser>` all resolve through the
existing generic registration. `BrokerAccountService` and `StockTransactionService` are already wired up under
`ScopedBusinessServicesRegister` — no new DI lines needed.

---

## 6. Tests

- `Fiolyn.Tests/Unit/Services/BrokerAccountServiceTests.cs` — full service surface: create-with-creator-as-first-member,
  membership filtering, soft-deleted member filtering, user-scoped soft-delete, member list / add (+ email
  normalization, + idempotent duplicate, + unknown email, + non-member caller), member remove (+ last-member
  `BadRequestException`, + non-member target, + non-member caller), admin list / get / soft-delete.
- `Fiolyn.Tests/Unit/Controllers/BrokerAccountControllerTests.cs` — caller-id plumbing, 404 vs 200 paths on all CRUD +
  HEAD, and the new member endpoints.
- `Fiolyn.Tests/Unit/Controllers/AdminBrokerAccountControllerTests.cs` — list with / without `userId` filter, get-by-id
  404 + 200, admin create with initial-member `AppUserId`, update, delete.
- `Fiolyn.Tests/Unit/Services/StockTransactionServiceTests.cs` — parser tests unchanged, plus membership-scoped create /
  get / list / update / delete, `CreateFromTelegramAsync` bypasses membership, admin overloads.
- `Fiolyn.Tests/Unit/Controllers/StockTransactionControllerTests.cs` — user-id plumbing, membership-scoped refetch in
  create, 404 on missing, update / delete.
- `Fiolyn.Tests/Unit/Controllers/AdminStockTransactionControllerTests.cs` — list / get / delete with unscoped admin
  calls.
- `Fiolyn.Tests/Unit/Services/TelegramMessageServiceTests.cs` — updated to expect `CreateFromTelegramAsync` (not
  `CreateAsync`).

---

## 7. Verification

1. `dotnet build Fiolyn.sln` — 0 warnings, 0 errors.
2. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — full suite passes.
3. Before deploy: `dotnet ef database update` from `Fiolyn/` to apply `BrokerAccountMultiMember`. Precondition: at least
   one active admin exists. Migration is strictly additive; old code continues to work after apply.
4. Manual smoke as admin A:
  - `GET /api/broker-accounts` → sees all pre-existing brokers (backfilled to A).
  - `POST /api/broker-accounts` `{ "name": "TCBS" }` → 201.
  - `POST /api/broker-accounts/{id}/members` `{ "email": "<B>" }` → 200 + two-member list.
5. Manual smoke as non-admin B:
  - `GET /api/broker-accounts` → sees TCBS (shared) but nothing else.
  - `POST /api/stock-transactions` with `BrokerAccountId = TCBS.Id` → 201.
  - As A: `GET /api/stock-transactions` → sees B's transaction (members are equal).
  - As B: `DELETE /api/broker-accounts/{TCBS}/members/{A.id}` → 204.
  - As A: `GET /api/stock-transactions/{tx}` → 404 (no longer a member of TCBS).
  - As B: `DELETE /api/broker-accounts/{TCBS}/members/{B.id}` → 400 (last member).
6. Manual smoke as admin on admin endpoints:
  - `GET /api/admin/broker-accounts` → list with full `Members`.
  - `POST /api/admin/broker-accounts` `{ "appUserId": X, "name": "..." }` → 201 with X as sole member.
  - `GET /api/admin/stock-transactions?userId=X&symbol=VNM` → scoped + filtered.
  - As a non-admin, any `/api/admin/...` call → 403.
7. Telegram ingest (hit the webhook with a `stock buy VNM 100 45000 4501500 broker:TCBS` message) → 200, transaction
   visible to all members of TCBS.

---

## Out of scope

- Roles on memberships — all members remain equal.
- Invite-and-accept flow — direct add-by-email is sufficient.
- Per-transaction creator tracking (`CreatorUserId` FK) — chose the simpler "broker members equal" model.
- Linking `TelegramUserInfo` to `AppUser` — the ingest path stays unscoped.
- Admin `POST`/`PUT` on transactions — admins self-join a broker if they need to write.
