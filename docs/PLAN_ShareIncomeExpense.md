# Share Income / Expense with Counterparties by Percent

## Context

`IncomeTransaction` and `ExpenseTransaction` only ever touch one `SpendAccount`. Real-world money frequently belongs partly to someone else — a group dinner, a household bill, a shared paycheck — and the only existing way to model that was to manually create a paired `LendTransaction` / `BorrowingTransaction`. This change adds a first-class way to mark "X% of this Income / Expense actually belongs to counterparty Y" with multiple counterparties allowed per row.

The chosen design **auto-creates paired `LendTransaction` / `BorrowingTransaction` rows** so the receivable / payable lives directly in the existing Lend / Borrow ledger that already handles counterparty, due date, settlement flow, balance accounting, and soft-delete. There is intentionally no separate share-metadata table — every "Bob owes me 30k" or "I owe Bob 30k" claim is just a regular Lend / Borrow row, linked back to the originating Income / Expense via a nullable `ParentTransactionId`.

**User-confirmed decisions:**
1. **Wire shape:** request carries `amount` (the *full* original amount, e.g. 100k) and `shares: [{ counterpartyName, percentage }, ...]`. The system splits internally.
2. **Atomic split via paired rows.** POST creates the parent + N child rows in a single save:
   - **Expense** with `amount=100`, `shares=[{Bob, 30}]` → `ExpenseTransaction.Amount = -70` and a linked `LendTransaction.Amount = -30` to Bob; `SpendAccount.Balance` drops by the full 100.
   - **Income** with `amount=100`, `shares=[{Bob, 30}]` → `IncomeTransaction.Amount = +70` and a linked `BorrowingTransaction.Amount = +30` to Bob; `SpendAccount.Balance` rises by the full 100.
3. **Linked lifecycle.** Nullable `ParentTransactionId` column on `TransactionBase`. PUT on the parent with a new shares list **replaces** linked children (delete-then-create). Soft-delete on the parent **cascades** soft-delete to linked children. Standalone Lend / Borrow rows (those with `ParentTransactionId == null`) are unaffected.
4. **Field copying for auto-children:** `SpendAccountId`, `Date`, `PayerId` copied from parent. `Description` becomes `"Shared with {counterpartyName}: {parentDescription}"`. `Category` stays null (parent's category is the wrong `Kind` for Lend / Borrow). `DueDate` stays null on auto-created rows; user can edit later via the standard `/api/lend-transactions/{id}` endpoint.
5. **Obligations endpoint** at `GET /api/obligations` exposes per-counterparty `Receivable` / `Payable` / `Net` aggregated over `LendTransaction` + `BorrowingTransaction` only — auto-created children are regular Lend / Borrow rows so they're already counted.
6. **Multiplicity:** N counterparties per transaction (max 20 in the request DTO). Each percentage ∈ [1, 100]. Sum ≤ 100. Duplicates (case-insensitive after counterparty resolve) rejected.
7. **Scope:** Income and Expense only. Lend / Borrow already carry a counterparty natively and stay untouched at the entity level.

---

## Data Model

`TransactionBase` gains:
```csharp
public long? ParentTransactionId { get ; set ; }
public TransactionBase? ParentTransaction { get ; set ; }
public ICollection<TransactionBase> Children { get ; set ; } = new List<TransactionBase>() ;
```
- Self-referential FK to `TransactionBase.Id`, `OnDelete.Restrict`. Soft-delete is the only path used in code; cascade-on-soft-delete is enforced in service-layer logic, not at the DB.
- Indexed on `ParentTransactionId` so the cascade-on-update / cascade-on-soft-delete subqueries are O(log n).
- The base column is nullable — only auto-created Lend / Borrow children populate it; standalone Lend / Borrow rows leave it null.

Migration `LinkChildTransactionsForSharing`:
- `ALTER TABLE "Transaction" ADD COLUMN "ParentTransactionId" bigint NULL`.
- FK to `Transaction.Id` with `ON DELETE RESTRICT`.
- Index `IX_Transaction_ParentTransactionId`.

---

## Service Layer

### `Services/Internal/BaseServices/ShareableTransactionServiceBase<T>`

Generic over `T : TransactionBase`. Sits between `StandardTransactionServiceBase<T>` and the Income / Expense leaf services. Constructor deps:
```csharp
IRepository<T> transactionRepository,
IRepository<SpendAccount> spendRepository,
IRepository<TransactionCategory> categoryRepository,
IRepository<Counterparty> counterpartyRepository,
IRepository<LendTransaction> lendRepository,
IRepository<BorrowingTransaction> borrowingRepository
```

Abstract hook so each leaf returns the right child type:
```csharp
protected abstract TransactionBase BuildChild( T parent, Counterparty counterparty, long childAmount ) ;
```
- Income leaf returns a `BorrowingTransaction { Counterparty = counterparty }`.
- Expense leaf returns a `LendTransaction { Counterparty = counterparty }`.
- The base wires `SpendAccountId`, `Date`, `PayerId`, the description prefix, and the parent + counterparty navs (`child.ParentTransaction = parent` / `child.Counterparty = counterparty` — never the scalar FKs, so EF threads parent / counterparty IDs through the same `SaveAsync`).

`CreateAsync(T parent, long userId, IReadOnlyList<TransactionShareInput> shares)`:
1. If `shares` is empty, short-circuit to `base.CreateAsync(parent, userId)` and return — single-row create path unchanged.
2. Membership check on `parent.SpendAccountId` (`StandardTransactionServiceBase.EnsureMembershipAsync`, promoted from `private` to `protected`).
3. Category-kind guard via `TransactionCategoryGuard.EnsureKindMatchesAsync`.
4. Resolve each share name through `CounterpartyResolver.ResolveOrCreateAsync` (per-user, case-insensitive). Validate `percentage ∈ [1, 100]`, sum ≤ 100, no duplicate counterparty (post-resolve by Id; for newly-staged rows whose Id is still 0, dedupe by trimmed name to catch "Bob" + "BOB" within one request).
5. Compute split:
   - `var totalAmount = parent.Amount ;` (signed: negative for Expense, positive for Income — controller sets sign before calling).
   - For each share: `childAmount = Math.Sign(totalAmount) * (Math.Abs(totalAmount) * percentage / 100)`.
   - `parent.Amount = totalAmount - sum(childAmount)` so the parent absorbs rounding loss.
6. For each share, call `BuildChild` then stage via `lendRepository.AddAsync` / `borrowingRepository.AddAsync` based on the concrete child type.
7. `transactionRepository.AddAsync(parent)`.
8. Apply balance: `var spend = await spendRepository.GetByIdOrThrowAsync(parent.SpendAccountId); spend.Balance += totalAmount ;`.
9. `await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(transactionRepository, parent.SpendAccountId)` — single save commits parent + children + any newly-staged counterparties.

Don't call `base.CreateAsync(parent, userId)` from the share path — it would save twice and apply only the parent's portion to the balance; we want the full `totalAmount` applied once.

`UpdateAsync(T parent, long userId, IReadOnlyList<TransactionShareInput> shares)`:
1. Project the *original* `parent.Amount`, original `SpendAccountId`, sum of existing linked children's amounts (`Sum(c.Amount where c.ParentTransactionId == parent.Id)`), and the new-spend-membership flag in one round-trip.
2. Resolve and validate the new share list as in Create.
3. Capture `newTotalAmount = parent.Amount` (the controller already mutated it to the new request total) before splitting.
4. Hard-delete every existing linked child (eager-loaded via the share base's `BuildReadQuery` override) via the matching child repo's `Delete()`; clear `parent.Children`.
5. Compute the new split; `parent.Amount` becomes the new portion. EF tracks the change via the already-tracked entity.
6. Stage new children.
7. Apply balance delta:
   - `oldTotalAmount = oldProjectedParentAmount + childrenSum`.
   - Same SpendAccount: `spend.Balance += (newTotalAmount - oldTotalAmount)`.
   - Cross-SpendAccount: reverse `oldTotalAmount` on the old account, apply `newTotalAmount` on the new account.
8. `SaveWithConcurrencyGuardAsync` once over both spend account ids.

`SoftDeleteAndReverseAsync` override (the standard base's hook, promoted from `private` to `protected virtual`):
1. `totalToReverse = entity.Amount + entity.Children.Sum(c => c.Amount)`.
2. `spend.Balance -= totalToReverse`.
3. Soft-delete parent + each child via the matching child repo.
4. Single save.

`BuildReadQuery` override (two parallel ThenIncludes, one per concrete child type — EF Core's translator only reliably resolves cast-includes against concrete TPT subtypes; interface casts in `ThenInclude` are not guaranteed):
```csharp
.Include( t => t.Children ).ThenInclude( c => ( c as LendTransaction )!.Counterparty )
.Include( t => t.Children ).ThenInclude( c => ( c as BorrowingTransaction )!.Counterparty )
```
Both ThenIncludes are emitted for both Income and Expense parents — the irrelevant cast yields a no-op LEFT JOIN at runtime. Same trick `TransactionReadService.BuildQuery` already uses for leaf-specific navs at the parent level.

### Leaf services

- `IncomeTransactionService` extends `ShareableTransactionServiceBase<IncomeTransaction>`, implements `BuildChild` → `BorrowingTransaction`.
- `ExpenseTransactionService` extends `ShareableTransactionServiceBase<ExpenseTransaction>`, implements `BuildChild` → `LendTransaction`.

### Other services

- `CounterpartyService.EnsureNotReferencedByActiveTransactionAsync` is the unchanged lend / borrow scan — auto-paired children are regular Lend / Borrow rows so the existing two-query check covers them. No new repo dep.
- `TransactionReadService.BuildQuery` adds `.Include(t => t.Children).ThenInclude(c => (c as IHasCounterparty)!.Counterparty)` so the unified read carries the linked children.
- `ObligationService` aggregates per-counterparty `Receivable` (sum of Lend magnitudes) / `Payable` (sum of Borrow magnitudes) / `Net`. Counterparties whose receivable + payable both equal zero are filtered out.

### `AppDbContext`

- Self-referential FK on `TransactionBase.ParentTransactionId` with `OnDelete.Restrict`.
- Index on `ParentTransactionId`.

---

## API Layer

### Request DTOs

`Controllers/DTOs/Requests/Shared/TransactionShareRequest.cs`:
```csharp
public record TransactionShareRequest(
  [Required] [MaxLength( 100 )] string CounterpartyName,
  [Required] [Range( 1, 100 )] int Percentage ) ;
```

`IncomeTransactionRequest` / `ExpenseTransactionRequest` carry an optional `[MaxLength(20)] List<TransactionShareRequest>? Shares = null`.

### Response DTOs

`Controllers/DTOs/Responses/Shared/LinkedShareResponse.cs`:
```csharp
public record LinkedShareResponse(
  long Id,                            // child Lend / Borrow row id
  TransactionKind Kind,               // Lend (Expense parent) or Borrowing (Income parent)
  long CounterpartyId,
  string CounterpartyName,
  long Amount,                        // signed, mirrors parent sign
  int Percentage )                    // recovered: |child.Amount| * 100 / |totalAmount|
;
```
Percentage is reconstructed on read from stored amounts (`totalAmount = parent.Amount + sum(children.Amount)`); integer division and the parent-absorbs-rounding rule keep round-trip exact for whole-percent inputs.

`IncomeTransactionResponse` / `ExpenseTransactionResponse` / unified `TransactionResponse` carry `LinkedShares: IReadOnlyList<LinkedShareResponse>`. Populated for Income / Expense parents from `t.Children` (already filtered to active by the global soft-delete query filter on `TransactionBase`); empty for everyone else.

### Controllers

`IncomeTransactionController` / `ExpenseTransactionController` pipe `request.Shares ?? []` to the service via the `MapShares` helper. Sign negation (`Amount = -request.Amount` for Expense) stays in the controller; the service splits.

---

## Files

**Created:**
- `Fiolyn/Controllers/DTOs/Requests/Shared/TransactionShareRequest.cs`
- `Fiolyn/Controllers/DTOs/Responses/Shared/LinkedShareResponse.cs`
- `Fiolyn/Controllers/DTOs/Responses/Obligation/ObligationResponse.cs`
- `Fiolyn/Controllers/ObligationController.cs`
- `Fiolyn/Services/Internal/Interfaces/IObligationService.cs`
- `Fiolyn/Services/Internal/ObligationService.cs`
- `Fiolyn/Services/Internal/BaseServices/ShareableTransactionServiceBase.cs`
- `Fiolyn/Services/Internal/BaseServices/TransactionShareInput.cs`
- `Fiolyn/Data/Migrations/*_LinkChildTransactionsForSharing.cs`
- `Fiolyn.Tests/Unit/Services/ExpenseTransactionServiceTests.cs`
- `Fiolyn.Tests/Unit/Services/ObligationServiceTests.cs`

**Modified:**
- `Fiolyn/Entities/TransactionBase.cs` — `ParentTransactionId` / `ParentTransaction` / `Children` navs.
- `Fiolyn/Data/AppDbContext.cs` — self-referential FK + index.
- `Fiolyn/Services/Internal/BaseServices/StandardTransactionServiceBase.cs` — promote `EnsureMembershipAsync` and `SoftDeleteAndReverseAsync` to `protected (virtual)`.
- `Fiolyn/Services/Internal/IncomeTransactionService.cs`, `ExpenseTransactionService.cs` — extend the shareable base + implement `BuildChild`.
- `Fiolyn/Services/Internal/Interfaces/IIncomeTransactionService.cs`, `IExpenseTransactionService.cs` — add `shares` parameter on Create / Update.
- `Fiolyn/Services/Internal/TransactionReadService.cs` — add the polymorphic children + counterparty include.
- `Fiolyn/Controllers/IncomeTransactionController.cs`, `ExpenseTransactionController.cs` — `MapShares` helper, pipe through.
- `Fiolyn/Controllers/DTOs/Requests/IncomeTransaction/IncomeTransactionRequest.cs`, `…/ExpenseTransaction/ExpenseTransactionRequest.cs` — optional `Shares` field.
- `Fiolyn/Controllers/DTOs/Responses/IncomeTransaction/IncomeTransactionResponse.cs`, `…/ExpenseTransaction/ExpenseTransactionResponse.cs` — `LinkedShares` field.
- `Fiolyn/Controllers/DTOs/Responses/Transaction/TransactionResponse.cs` — `LinkedShares` field on the unified shape.
- `Fiolyn/ServiceRegisters/ScopedBusinessServicesRegister.cs` — register `IObligationService`.
- `Fiolyn.Tests/Unit/Services/IncomeTransactionServiceTests.cs` — paired-Borrowing-child cases (split, rounding, validation, cascade soft-delete).
- `Fiolyn.Tests/Unit/Controllers/IncomeTransactionControllerTests.cs` — adapt mock signature.
- `.claude/CLAUDE.md` — document the new auto-paired Lend / Borrow rule + obligations endpoint.

---

## Reuse (don't reinvent)

- `Services/Internal/Helpers/CounterpartyResolver.ResolveOrCreateAsync` — same per-user resolve-or-stage flow.
- `Services/Internal/Helpers/TransactionBalanceWriter.SaveWithConcurrencyGuardAsync` — single-save concurrency translator over the parent + children stage set.
- `Services/Internal/Helpers/TransactionCategoryGuard.EnsureKindMatchesAsync` — for the parent's category. Auto-children stay uncategorised.
- `IRepository<LendTransaction>` / `IRepository<BorrowingTransaction>` — direct staging via `AddAsync` / `Delete` / `SoftDelete`. We bypass `LendTransactionService` / `BorrowingTransactionService` so we don't fan out to multiple `SaveAsync`s; orchestration is internal to `ShareableTransactionServiceBase`.
- The polymorphic `(c as IHasCounterparty)!.Counterparty` include pattern is the same trick `TransactionReadService.BuildQuery` already uses.
- Children referenced by parents staged in the same transaction must use `child.ParentTransaction = parent`, never `child.ParentTransactionId = parent.Id` — same rule used by `CounterpartyTransactionServiceBase` and documented in `.claude/CLAUDE.md`.

---

## Verification

1. **Build:** `dotnet build Fiolyn/Fiolyn.csproj` clean (the pre-existing `AppUser` ↔ `BrokerAccountMember` warning is unrelated).
2. **Migration:** `cd Fiolyn ; dotnet ef migrations add LinkChildTransactionsForSharing --output-dir Data/Migrations`. Inspect generated SQL: `ALTER TABLE "Transaction" ADD COLUMN "ParentTransactionId" bigint NULL` plus FK + index. Then `dotnet ef database update` against local Postgres.
3. **Targeted tests:**
   ```
   dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~IncomeTransactionService|FullyQualifiedName~ExpenseTransactionService|FullyQualifiedName~CounterpartyService|FullyQualifiedName~TransactionReadService|FullyQualifiedName~ObligationService|FullyQualifiedName~IncomeTransactionController|FullyQualifiedName~ExpenseTransactionController"
   ```
   Cases:
   - Create with empty shares behaves identically to today (no children, full Amount on parent).
   - Create with `shares=[{Bob, 30}]` produces `parent.Amount=±70`, one child Lend / Borrow with `Amount=±30`, `parent.Children` populated, balance moved by full ±100.
   - Create with `shares=[{Bob, 33}, {Alice, 33}, {Carol, 33}]` — parent absorbs the rounding, totals reconcile.
   - Sum of percentages > 100 → `BadRequestException`, no rows staged.
   - Duplicate counterparty (case-insensitive) → `BadRequestException`.
   - Update with new shares replaces existing children (old hard-deleted, balance reversed; new staged, balance applied).
   - Update across SpendAccount: old account reversed by old total, new account applied by new total.
   - SoftDelete cascades to children: each child also soft-deleted, balance reversed by total.
   - Counterparty soft-delete blocked while an auto-created Lend / Borrow child references it.
   - `GET /api/transactions` shows linked children inline on the parent's `linkedShares` array.
   - `GET /api/obligations` includes the auto-Lend in receivable.
4. **Pre-commit gate:** full suite — `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` green.
5. **End-to-end smoke (manual):**
   - `POST /api/expense-transactions` `{ amount: 100000, shares: [{counterpartyName: "Bob", percentage: 30}] }` → 201; response shows `amount: -70000` and `linkedShares[0].amount: -30000`. `GET /api/lend-transactions` shows the matching Lend row to Bob. Balance dropped by 100k.
   - `PUT` the same expense with `shares: [{Bob, 50}, {Alice, 25}]` → response `amount: -25000`, two `linkedShares` rows. Old Lend gone, new Lends present. Balance unchanged from totals (still ±100k aggregate).
   - `DELETE` the expense → soft-deleted; `GET /api/lend-transactions` no longer shows the linked Lends. Balance restored.
   - `GET /api/obligations` reflects the receivable through the lifecycle.
