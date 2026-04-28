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

`StockTransactionService` is the single owner of the delta math. All three mutating paths (`CreateAsync`, `UpdateAsync`, `SoftDeleteAsync`) compute a signed delta, load the `SpendAccount` entity via `IRepository<SpendAccount>.GetByIdOrThrowAsync`, mutate `Balance += delta`, then call a single `SaveAsync` that flushes both the balance change and the transaction insert/update together.

`SpendAccount.Balance` carries `[ConcurrencyCheck]`, so EF adds `WHERE Balance = @original` to every SpendAccount UPDATE. If two concurrent operations race on the same account, the loser's `SaveAsync` throws `DbUpdateConcurrencyException`, which `StockTransactionService.SaveWithConcurrencyGuardAsync` rethrows as `ConflictException` (409). Because both changes land in the same `SaveAsync`, EF's implicit transaction provides atomicity — no separate `ExecuteUpdate` or explicit transaction wrapper is needed.

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

### `BrokerAccountService` (`Fiolyn/Services/Internal/BrokerAccountService.cs`)

- Injects `IRepository<SpendAccount> spendRepository`.
- Private helper `EnsureUserIsSpendAccountMemberAsync(spendId, userId)` mirrors `EnsureCallerIsMemberAsync`.
- `CreateAsync(account, creatorUserId)` validates spend-account membership before `AddAsync`.
- `UpdateAsync(account, callerUserId)` re-reads the original `SpendAccountId` via an `AsNoTracking` projection and only re-validates membership when the value changed.
- Admin `UpdateAsync(account)` overload skips the per-user check but still verifies the target spend account exists.

### `StockTransactionService` (`Fiolyn/Services/Internal/StockTransactionService.cs`)

Injects `IRepository<StockTransaction>`, `IRepository<BrokerAccount>`, `IRepository<SpendAccount>`.

- **`CreateAsync`** — one fused query (`Query().Where(member-filter).Select(SpendAccountId).FirstOrDefaultAsync()`) returns the linked spend ID or null → 404. Load the spend account, apply `+delta` to `Balance`, add the transaction, single `SaveAsync`.
- **`UpdateAsync`** — one combined projection fetches `(TransactionType, TotalAmount, BrokerAccountId, OldSpendAccountId)` with a correlated subquery for the new broker's `SpendAccountId` (null → 404 "broker not member"). Same-broker: load the single spend account, apply `newDelta − oldDelta`; broker-change: load both spend accounts, reverse old delta on the old, apply new delta on the new. Then `Update` + single `SaveAsync`.
- **`SoftDeleteAsync(id, userId)`** — `GetByIdForUserAsync` eager-loads `BrokerAccount`; load the spend account via `spendRepository`, apply `−delta`, soft-delete transaction, single `SaveAsync`.
- **Admin `SoftDeleteAsync(id)`** — same, loading the transaction via `Query().Include(BrokerAccount).FirstOrDefaultAsync()`.

`SaveWithConcurrencyGuardAsync(spendAccountId)` wraps `SaveAsync`, catching `DbUpdateConcurrencyException` → `ConflictException` (409) for all three paths.

### `SpendAccountService` (`Fiolyn/Services/Internal/SpendAccountService.cs`)

Injects `IRepository<BrokerAccount> brokerRepository`. `AdjustBalanceForUserAsync` / `AdjustBalanceAsync` delegate to a private `ApplyAndReadBalanceAsync(id, delta)`: load via `GetByIdOrThrowAsync`, mutate `Balance += delta`, call `SaveAsync` (catching `DbUpdateConcurrencyException` → `ConflictException`), return the new `Balance`. Both soft-delete overloads guard:
```csharp
var stillReferenced = await brokerRepository.Query()
  .AnyAsync( b => b.SpendAccountId == id ) ;
if ( stillReferenced )
{
  throw new BadRequestException(
    $"SpendAccount {id} is linked from one or more broker accounts — unlink them first." ) ;
}
```

This prevents a `BrokerAccount` from being orphaned to a soft-deleted spend account, which would otherwise blow up `GetByIdOrThrowAsync(spendId)` on the next stock transaction.

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
- Test fixture stubs `IRepository<SpendAccount>` via `SetupSpendAccount(id, initialBalance)`, returning a real `SpendAccount` instance so `Balance` mutations are observable.
- Buy applies `−TotalAmount`; Sell and Dividend apply `+TotalAmount`.
- Update with same broker: `spendAccount.Balance` reflects `newDelta − oldDelta`.
- Update with broker change: old spend account is reversed, new spend account receives the new delta.
- Update with no change: `spendRepository.GetByIdOrThrowAsync` is not called (skipped when `netDelta == 0`).
- Soft-delete reverses the original delta.
- Membership/projection failures don't reach `spendRepository`.

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
- `IRepository<T>.Query()` for membership and original-value reads.
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
