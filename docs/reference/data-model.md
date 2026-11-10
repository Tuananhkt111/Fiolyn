# Data model

Entities, keys, foreign keys and delete behaviour as mapped by `Fiolyn/Data/AppDbContext.cs`.

> **Canonical rules:** [rules/ef-pitfalls.md](../rules/ef-pitfalls.md)

## Entities

| Entity | Table | Auditable | Soft delete | Notes |
|---|---|---|---|---|
| `AppUser` | `AppUser` | ✅ | ✅ | Unique index on `TelegramId` |
| `AllowedEmail` | `AllowedEmail` | ✅ | ✅ | Unique index on `Email` |
| `RefreshToken` | `RefreshToken` | ✅ | — | Explicit revocation via `RevokedAt` |
| `SpendAccount` | `SpendAccount` | ✅ | ✅ | `Balance` carries `[ConcurrencyCheck]` |
| `SpendAccountMember` | `SpendAccountMember` | — | — | Join: composite PK, `JoinedAt` only |
| `BrokerAccount` | `BrokerAccount` | ✅ | ✅ | Required FK to `SpendAccount` |
| `BrokerAccountMember` | `BrokerAccountMember` | — | — | Join: composite PK, `JoinedAt` only |
| `Counterparty` | `Counterparty` | ✅ | ✅ | Per-user; `Name` setter trims |
| `TransactionCategory` | `TransactionCategory` | ✅ | ✅ | Unique index on `(Kind, Name)` |
| `TransactionBase` (abstract) | `Transaction` | ✅ | ✅ | TPT base — see below |

`RefreshToken` is the only entity that revokes rather than soft-deletes: rotation needs to
distinguish "replaced" from "deleted", and `RevokedAt` carries `[ConcurrencyCheck]` so two
concurrent refreshes on the same token race deterministically.

## Transaction hierarchy

Table-per-type via `UseTptMappingStrategy()`. The base table `Transaction` owns the id sequence;
each leaf table shares that primary key.

```
TransactionBase ─ "Transaction"      SpendAccountId, CategoryId?, Date, Description,
  │                                   PayerId?, Amount (signed), ParentTransactionId?, audit
  ├── IncomeTransaction              (no extra columns)
  ├── ExpenseTransaction             (no extra columns)
  ├── AdjustmentTransaction          (no extra columns)
  ├── LendTransaction                DueDate?, CounterpartyId
  ├── BorrowingTransaction           DueDate?, CounterpartyId
  ├── TransferTransaction            DestinationSpendAccountId
  └── StockTransaction               Symbol, StockTransactionType, Quantity, Price,
                                      BrokerAccountId
```

`Amount` is signed and is what moves `SpendAccount.Balance` — negative is money out, positive is
money in. Three leaves add no columns of their own; they exist so the kind is a type rather than a
discriminator column, which keeps leaf-specific FKs (`CounterpartyId`, `BrokerAccountId`) NOT NULL.

`ParentTransactionId` is a nullable self-reference. It is set only on Lend/Borrow rows auto-created
by sharing an income or expense — see [0007](../decisions/0007-shares-as-paired-lend-borrow-rows.md).

Money and quantity columns are `long`, never `decimal` — see
[0001](../decisions/0001-money-as-long-not-decimal.md).

## Foreign keys and delete behaviour

Configured in `OnModelCreating`. EF's default for a required relationship is `Cascade`; every
override below is deliberate.

| From | To | Behaviour | Why |
|---|---|---|---|
| `BrokerAccount` | `SpendAccount` | `Restrict` | Cascade would delete brokers with the account and forms a dependency loop — [0010](../decisions/0010-broker-spend-account-fk-restrict.md) |
| `TransactionBase` | `SpendAccount` (source) | `Restrict` | Ledger history must outlive an account delete attempt |
| `TransferTransaction` | `SpendAccount` (destination) | `Restrict` | Mirrors the source side |
| `TransactionBase` | `TransactionCategory` | `SetNull` | Retiring a category must not destroy history |
| `TransactionBase` | `AppUser` (payer) | `SetNull` | Removing a user must not cascade through the ledger |
| `Counterparty` | `AppUser` (owner) | `Restrict` | Contact list is not cascade-deleted with its owner |
| `LendTransaction` | `Counterparty` | `Restrict` | Blocks hard delete; service blocks soft delete |
| `BorrowingTransaction` | `Counterparty` | `Restrict` | Same |
| `TransactionBase` | `TransactionBase` (parent) | `Restrict` | Share children cascade in service code, not at the DB |

**`Restrict` only guards hard deletes.** Fiolyn soft-deletes, which no FK can see, so the real
guards live in service code:

- `SpendAccountService` blocks soft-delete while an active broker references the account, or an
  active transaction references it as source or transfer destination.
- `CounterpartyService` blocks soft-delete while an active lend or borrow references it.

## Soft-delete query filters

Registered globally, so `IRepository.Query()` returns active rows by default. Chain
`.IgnoreQueryFilters()` to see deleted ones. No unique index is filtered on `IsDeleted`: a deleted
row still holds its unique value, which is why the create paths restore a deleted twin
([0024](../decisions/0024-recreating-a-deleted-row-restores-it.md)).

| Entity | Filter |
|---|---|
| `AppUser`, `AllowedEmail`, `SpendAccount`, `BrokerAccount`, `TransactionBase`, `TransactionCategory` | `!e.IsDeleted` |
| `Counterparty` | `!e.IsDeleted` — **deliberately not** also `!e.AppUser.IsDeleted` |
| `SpendAccountMember`, `BrokerAccountMember` | `!m.AppUser.IsDeleted` |
| `RefreshToken` | `!r.User.IsDeleted` |

Filters apply transitively — through `.Include` and through nav traversals such as
`b.Members.Any(m => ...)`. `IRepository.Query()` is filtered; `IRepository.GetByIdAsync` wraps
`FindAsync` and is **not**.

Both facts are traps when writing queries. See
[Soft-delete query filter](../rules/ef-pitfalls.md#soft-delete-query-filter) for what that means
in practice, and [counterparty.md](../rules/counterparty.md#dont-double-filter-the-soft-delete-query-filter)
for why `Counterparty`'s filter is deliberately narrower.

## Join entities

`SpendAccountMember` and `BrokerAccountMember` follow the same shape:

- `[PrimaryKey(AccountId, AppUserId)]` — prevents duplicate membership at the database level.
- `[Index(AppUserId, AccountId)]` — the reverse index. Without it, "which accounts is this user in?"
  cannot use the PK index, whose leftmost column is the account.
- No `IAuditable`, no `ISoftDelete`. A membership row is presence, not state: joining is an insert,
  leaving is a hard delete, and `JoinedAt` is the only fact worth keeping.

## Auditing

`AppDbContext.SaveChangesAsync` stamps every `IAuditable` entry before delegating to the base:
`Created` on insert, `LastModified` on insert and update. Services never set these by hand.

## Migrations

One migration, `20260510023914_InitialCreate`, creates the whole schema — the earlier history was
squashed, see [0005](../decisions/0005-squashed-migration-history.md). Migrations are never applied
at startup, see [0004](../decisions/0004-migrations-never-run-on-startup.md).

`DbSet` names are singular (`StockTransaction`, not `StockTransactions`).
