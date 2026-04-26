# BrokerAccount → SpendAccount Link & Auto-Balance Adjustment

## Context

`SpendAccount.Balance` was previously a free-form `long` mutated only by `PUT /api/spend-accounts/{id}`. There was no link from a `BrokerAccount` to a `SpendAccount`, so a stock buy/sell/dividend recorded against a broker had no effect on any spend balance — users had to manually update the balance to keep the books in sync.

This change introduces a `1 SpendAccount → N BrokerAccount` link so that **every stock transaction automatically adjusts the linked spend account's balance** by `TotalAmount`.

### Sign convention

- **Buy** → `Balance -= TotalAmount` (cash leaves the spend account)
- **Sell** → `Balance += TotalAmount` (cash returns)
- **Dividend** → `Balance += TotalAmount` (cash returns)

### Design decisions

- The link is **required** for every `BrokerAccount` (NOT NULL FK).
- **No retroactive recompute** when a broker's `SpendAccountId` changes — only future transactions move the new balance.
- The caller setting/changing `BrokerAccount.SpendAccountId` **must be a member of both** the broker and the target spend account.
- Soft-deleting a `SpendAccount` is blocked while any active `BrokerAccount` still references it.

---

## How it works (runtime)

`StockTransactionService` is the single owner of the delta math. All three mutating paths (`CreateAsync`, `UpdateAsync`, `SoftDeleteAsync`) compute a signed delta and feed it to `ISpendAccountBalanceWriter.ApplyDeltaAsync(spendId, delta)`, which issues an atomic SQL `Balance = Balance + @delta` (and bumps `LastModified`) via EF Core's `ExecuteUpdate`. The atomic relative update is the design's core safety property — two concurrent stock-transaction operations on the same spend account cannot lose updates the way a read-modify-write `spend.Balance += delta` would.

Because `ExecuteUpdate` runs SQL outside `SaveChanges`, the balance write and the transaction insert/update are wrapped in `IUnitOfWork.ExecuteInTransactionAsync` so both land in one DB transaction (or neither).

```csharp
private static long ComputeBalanceDelta( TransactionType type, long totalAmount ) =>
  type switch
  {
    TransactionType.Buy      => -totalAmount,
    TransactionType.Sell     =>  totalAmount,
    TransactionType.Dividend =>  totalAmount,
    _                        =>  0L
  } ;
```

- **Create:** one fused query resolves broker-membership + `SpendAccountId` (returns null on either miss → 404). Apply `+delta` to that spend account.
- **Update:** broker-membership check + a single `AsNoTracking` projection of the original `(TransactionType, TotalAmount, BrokerAccountId, OldSpendAccountId)` (the spend ID rides along via the `BrokerAccount` nav). Same-broker case applies `(newDelta − oldDelta)` in one writer call; broker-change case reverses `oldDelta` on the old spend and applies `newDelta` on the new spend.
- **Soft-delete (user):** `GetByIdForUserAsync` already eager-loads `BrokerAccount`, so `transaction.BrokerAccount.SpendAccountId` is in memory — apply `−delta` directly.
- **Soft-delete (admin):** single `Include(BrokerAccount).FirstOrDefaultAsync` to fetch transaction + spend ID together; apply `−delta`.

The Telegram ingest path (`TelegramMessageService → StockTransactionService.CreateAsync`) inherits the auto-adjust behaviour automatically — no Telegram-side changes.

---

## Data model

### `Fiolyn/Entities/BrokerAccount.cs`

Just `[Required] long SpendAccountId` and a non-nullable `SpendAccount` nav — EF Core's FK convention picks up the relationship from the property name and auto-creates an index on `SpendAccountId`, so no `[ForeignKey]` or `[Index]` attributes are needed.

```csharp
[ Required ]
public long SpendAccountId { get ; set ; }

public SpendAccount SpendAccount { get ; set ; } = null! ;
```

The relationship's `OnDelete` is the only thing that needs Fluent — there's no `[DeleteBehavior]` attribute equivalent. `AppDbContext.OnModelCreating` overrides the default `Cascade` to `Restrict`:

```csharp
modelBuilder.Entity<BrokerAccount>()
  .HasOne( b => b.SpendAccount )
  .WithMany()
  .HasForeignKey( b => b.SpendAccountId )
  .OnDelete( DeleteBehavior.Restrict ) ;
```

Cascade would (a) silently delete every broker if a spend account is hard-deleted and (b) form a multi-cascade chain that EF flags as a dependency loop. Soft-deletion is blocked by `SpendAccountService` while any active broker references the account.

### Migrations (two-step)

Existing rows must get a value before the FK can be enforced NOT NULL. Done as two migrations with a manual backfill in between:

**Migration A — `LinkBrokerAccountToSpendAccountNullable`**
- Adds `SpendAccountId` as nullable `bigint`.
- Adds the FK to `SpendAccount(Id)`.
- Adds an index on `SpendAccountId`.

**Backfill (manual SQL, run between A and B)**
```sql
UPDATE "BrokerAccount" b
SET    "SpendAccountId" = <chosen spend account id>
WHERE  b."SpendAccountId" IS NULL ;
```
Run in dev and prod (Neon) before applying B.

**Migration B — `RequireBrokerAccountSpendAccountId`**
- Alters `SpendAccountId` to NOT NULL.

---

## Service layer

### `ISpendAccountBalanceWriter` (`Fiolyn/Services/Internal/Interfaces/ISpendAccountBalanceWriter.cs` + `SpendAccountBalanceWriter.cs`)

A focused single-method abstraction over the atomic SQL update:

```csharp
public async Task ApplyDeltaAsync( long spendAccountId, long delta )
{
  if ( delta == 0L ) return ;

  var rows = await spendRepository.QueryActive()
    .Where( s => s.Id == spendAccountId )
    .ExecuteUpdateAsync( setters => setters
      .SetProperty( s => s.Balance, s => s.Balance + delta )
      .SetProperty( s => s.LastModified, _ => DateTime.UtcNow ) ) ;

  if ( rows == 0 )
  {
    throw new KeyNotFoundException( $"SpendAccount {spendAccountId} not found." ) ;
  }
}
```

Bypasses the change tracker (and therefore `IAuditable.LastModified` plumbing in `AppDbContext.SetDefaultValues`), so `LastModified` is set in the same statement. Registered in `ScopedBusinessServicesRegister`.

### `IUnitOfWork` (`Fiolyn/Data/Repositories/IUnitOfWork.cs` + `UnitOfWork.cs`)

A separate primitive for explicit transactions, kept off `IRepository<T>` because transactions are `DbContext`-scoped, not entity-scoped:

```csharp
public async Task ExecuteInTransactionAsync( Func<Task> action )
{
  await using var transaction = await dbContext.Database.BeginTransactionAsync() ;
  await action() ;
  await transaction.CommitAsync() ;
}
```

Rollback is handled by `await using` disposal: if `action` throws, the transaction is disposed without commit and Npgsql rolls back. Registered in `RepositoriesServiceRegister`.

### `BrokerAccountService` (`Fiolyn/Services/Internal/BrokerAccountService.cs`)

- Injects `IRepository<SpendAccount> spendRepository`.
- Private helper `EnsureUserIsSpendAccountMemberAsync(spendId, userId)` mirrors `EnsureCallerIsMemberAsync`.
- `CreateAsync(account, creatorUserId)` validates spend-account membership before `AddAsync`.
- `UpdateAsync(account, callerUserId)` re-reads the original `SpendAccountId` via an `AsNoTracking` projection and only re-validates membership when the value changed.
- Admin `UpdateAsync(account)` overload skips the per-user check but still verifies the target spend account exists.

### `StockTransactionService` (`Fiolyn/Services/Internal/StockTransactionService.cs`)

Injects `IRepository<StockTransaction>`, `IRepository<BrokerAccount>`, `ISpendAccountBalanceWriter`, `IUnitOfWork`.

- **`CreateAsync`** — one fused query (`QueryActive().Where(member-filter).Select(SpendAccountId).FirstOrDefaultAsync()`) returns the linked spend ID or null → 404. Wrap `ApplyDeltaAsync(+delta)` + `AddAsync` + `SaveAsync` in `unitOfWork.ExecuteInTransactionAsync`.
- **`UpdateAsync`** — one combined projection: filters by `t.Id == transactionId`, fetches `(TransactionType, TotalAmount, BrokerAccountId, OldSpendAccountId)`, and joins a correlated subquery on the *new* broker that returns `NewSpendAccountId` only if the caller is a member. Outer null → 404 "transaction not found"; `NewSpendAccountId` null → 404 "broker not member". Both surface the same to the API. Inside the transaction: same-spend (broker unchanged) → one `ApplyDeltaAsync(newDelta − oldDelta)`; broker-change → `ApplyDeltaAsync(-oldDelta)` on old spend + `ApplyDeltaAsync(+newDelta)` on new spend; then `Update` + `SaveAsync`.
- **`SoftDeleteAsync(id, userId)`** — `GetByIdForUserAsync` already eager-loads `BrokerAccount`; read `SpendAccountId` directly off `transaction.BrokerAccount`. Inside the transaction: `ApplyDeltaAsync(-delta)` + `SoftDelete` + `SaveAsync`.
- **Admin `SoftDeleteAsync(id)`** — single `QueryActive().Include(BrokerAccount).FirstOrDefaultAsync()` to fetch the transaction + linked spend ID together; same body inside the transaction.

### `SpendAccountService` (`Fiolyn/Services/Internal/SpendAccountService.cs`)

Inject `IRepository<BrokerAccount> brokerRepository`. Both soft-delete overloads guard:
```csharp
var stillReferenced = await brokerRepository.QueryActive()
  .AnyAsync( b => b.SpendAccountId == id ) ;
if ( stillReferenced )
{
  throw new BadRequestException(
    $"SpendAccount {id} is linked from one or more broker accounts — unlink them first." ) ;
}
```

This prevents a `BrokerAccount` from being orphaned to a soft-deleted spend account, which would otherwise blow up `GetActiveByIdOrThrowAsync(spendId)` on the next stock transaction.

---

## DTOs

- `Controllers/DTOs/Requests/BrokerAccount/BrokerAccountRequest.cs` — add `[Required] long SpendAccountId`.
- `Controllers/DTOs/Requests/BrokerAccount/AdminBrokerAccountCreateRequest.cs` — same.
- `Controllers/DTOs/Responses/BrokerAccount/BrokerAccountResponse.cs` — add `SpendAccountId` (and optionally `SpendAccountName` via `.Include(b => b.SpendAccount)` in the read paths).
- `Controllers/DTOs/Responses/BrokerAccount/AdminBrokerAccountResponse.cs` — same.

`BrokerAccountController.CreateAsync` / `UpdateAsync` set `brokerAccount.SpendAccountId = request.SpendAccountId`.

---

## Tests

`Fiolyn.Tests/Unit/Services/`

**`BrokerAccountServiceTests.cs`**
- `CreateAsync` throws `KeyNotFoundException` when the caller is not a member of the target `SpendAccountId`.
- `UpdateAsync` re-validates spend-account membership only when `SpendAccountId` changes.

**`StockTransactionServiceTests.cs`**
- Test fixture mocks `IUnitOfWork.ExecuteInTransactionAsync` as a pass-through and accumulates `ISpendAccountBalanceWriter.ApplyDeltaAsync` calls into a per-spend-account `Dictionary<long, long>` so assertions read net deltas, not balances.
- Buy applies `−TotalAmount`; Sell and Dividend apply `+TotalAmount`.
- Update with same broker: writer is called once with `(newDelta − oldDelta)`.
- Update with broker change: two writer calls — old spend reverses, new spend applies.
- Update with no change: writer is called once with `0` (the writer short-circuits internally).
- Soft-delete reverses the original delta.
- Membership/projection failures don't reach the writer or the unit-of-work.

**`SpendAccountServiceTests.cs`**
- Soft-delete throws `BadRequestException` while a `BrokerAccount` references it; succeeds once the broker is deleted/repointed.

`Fiolyn.Tests/Unit/Controllers/`

- `BrokerAccountControllerTests.cs` — fixtures include `SpendAccountId` in `BrokerAccountRequest`.
- `StockTransactionControllerTests.cs` — no controller-layer behaviour change; existing tests pass unchanged.

---

## `CLAUDE.md` updates

Per the repo's standing rule (`feedback_update_claude_md`), update `.claude/CLAUDE.md`:

- Under **Architecture** / **Key Rules**, add: *"Every `BrokerAccount` references a `SpendAccount` (NOT NULL FK). Stock transactions auto-adjust the linked balance: Buy → `−TotalAmount`, Sell/Dividend → `+TotalAmount`. Update reverses the old delta and applies the new; soft-delete reverses. Logic lives in `StockTransactionService`."*
- Mention that the caller setting `SpendAccountId` on a broker must be a member of both accounts.
- Note that `SpendAccount` soft-delete is blocked while any active broker still references it.

---

## Critical files

**Entity / EF model**
- `Fiolyn/Entities/BrokerAccount.cs`
- `Fiolyn/Data/AppDbContext.cs` (single Fluent rule for `OnDelete: Restrict`)
- `Fiolyn/Data/Migrations/20260426024118_LinkBrokerAccountToSpendAccountNullable.cs`
- `Fiolyn/Data/Migrations/20260426032716_RequireBrokerAccountSpendAccountId.cs`

**New infrastructure**
- `Fiolyn/Data/Repositories/IUnitOfWork.cs` + `UnitOfWork.cs`
- `Fiolyn/Services/Internal/Interfaces/ISpendAccountBalanceWriter.cs` + `Fiolyn/Services/Internal/SpendAccountBalanceWriter.cs`
- `Fiolyn/ServiceRegisters/RepositoriesServiceRegister.cs` (registers `IUnitOfWork`)
- `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs` (registers `ISpendAccountBalanceWriter`)

**Service layer**
- `Fiolyn/Services/Internal/BrokerAccountService.cs` + `Interfaces/IBrokerAccountService.cs`
- `Fiolyn/Services/Internal/StockTransactionService.cs`
- `Fiolyn/Services/Internal/SpendAccountService.cs` (soft-delete guard)

**Controllers / DTOs**
- `Fiolyn/Controllers/BrokerAccountController.cs`, `AdminBrokerAccountController.cs`
- `Controllers/DTOs/Requests/BrokerAccount/{BrokerAccountRequest, AdminBrokerAccountCreateRequest}.cs`
- `Controllers/DTOs/Responses/BrokerAccount/{BrokerAccountResponse, AdminBrokerAccountResponse}.cs`

**Tests**
- `Fiolyn.Tests/Unit/Services/{StockTransactionServiceTests, BrokerAccountServiceTests, SpendAccountServiceTests}.cs`
- `Fiolyn.Tests/Unit/Controllers/{BrokerAccountControllerTests, AdminBrokerAccountControllerTests}.cs`

**Docs**
- `.claude/CLAUDE.md`
- `docs/BrokerAccountSpendAccountLink.md` (this file)

Reused existing primitives:
- `IRepository<T>.QueryActive()` for membership and original-value reads.
- The "filter via parent's `Members.Any(...)`" pattern (membership check inlined into the combined projection in `CreateAsync` / `UpdateAsync` — no separate helper).
- `BadRequestException` (`Infrastructure/Exceptions/`) for the SpendAccount-still-referenced case, mirroring `BrokerAccountService.RemoveMemberAsync`.

---

## Verification

From `Fiolyn/`:
1. `dotnet build` — must compile clean.
2. `dotnet ef migrations add LinkBrokerAccountToSpendAccountNullable --output-dir Data/Migrations` — confirm column is nullable and FK is generated.
3. `dotnet ef database update` — applies migration A.
4. Run the backfill SQL on each environment.
5. `dotnet ef migrations add RequireBrokerAccountSpendAccountId --output-dir Data/Migrations` — confirm it only contains an `AlterColumn` to NOT NULL.
6. `dotnet ef database update` — applies migration B.

From repo root, run only the affected test classes (per `CLAUDE.md` workflow):
```bash
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj \
  --filter "FullyQualifiedName~StockTransactionServiceTests|FullyQualifiedName~BrokerAccountServiceTests|FullyQualifiedName~SpendAccountServiceTests|FullyQualifiedName~BrokerAccountControllerTests"
```
Pre-commit gate: `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` (full suite must pass).

End-to-end smoke (`dotnet run` locally with seeded data):
- Create `SpendAccount` "Cash" with `Balance = 1_000_000`.
- Create `BrokerAccount` "VPS" pointing to it.
- `POST /api/stock-transactions` Buy SSI 100 @ 25_000 total 2_500_000 → expect `Cash.Balance = −1_500_000`.
- `PUT` that transaction to Sell with the same total → expect `Cash.Balance = +3_500_000` (reverse −2.5M, apply +2.5M, net +5M from the buy state).
- `DELETE` the transaction → expect `Cash.Balance` returns to `+1_000_000`.
- Send a `stock buy SSI 100 25000 2500000 b:VPS` Telegram message → same Buy result on `Cash`.
- Try to soft-delete `Cash` while VPS still references it → 400.
