# Plan: StockTransaction Entity + Telegram Parsing + CRUD API

## Context

The user wants to save stock transactions (Buy, Sell, Dividend) to PostgreSQL. The data flows in two ways: (1) a
structured message sent via Telegram is parsed and saved automatically, (2) a dedicated REST API allows manual inserts.
Each transaction tracks symbol, type, quantity, price, total amount (including fees), date, broker, and optional notes.

---

## 1. Entity Design

### `Fiolyn/Enums/TransactionType.cs` _(new)_

```csharp
public enum TransactionType
{
  Buy,
  Sell,
  Dividend
}
```

### `Fiolyn/Entities/StockTransaction.cs` _(new)_

Implements `IAuditable` + `ISoftDelete`.

| Field             | Type                      | Notes                             |
|-------------------|---------------------------|-----------------------------------|
| `Id`              | `long`                    | PK, auto-increment                |
| `Symbol`          | `string` (MaxLength 20)   | Ticker, e.g. `VNM`, `AAPL`        |
| `TransactionType` | `TransactionType` enum    | Buy / Sell / Dividend             |
| `Quantity`        | `decimal`                 | Shares traded; `0` for Dividend   |
| `Price`           | `decimal`                 | Price per share; `0` for Dividend |
| `TotalAmount`     | `decimal`                 | Actual cash out/in including fees |
| `TransactionDate` | `DateTime`                | UTC; date of the trade            |
| `Broker`          | `string` (MaxLength 100)  | Broker/account name               |
| `Notes`           | `string` (MaxLength 500)  | Free-text memo; defaults to `""`  |
| `Created`         | `DateTime`                | Auto-populated by `AppDbContext`  |
| `LastModified`    | `DateTime`                | Auto-populated by `AppDbContext`  |
| `IsDeleted`       | `bool`                    | Soft-delete flag                  |

---

## 2. Data Layer

### `Fiolyn/Data/AppDbContext.cs` _(modify)_

Add: `public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();`

### Migration

```bash
dotnet ef migrations add AddStockTransaction --output-dir Data/Migrations
```

Run from `Fiolyn/` subdirectory.

---

## 3. Service Layer

### `Fiolyn/Services/External/Stock/IStockTransactionService.cs` _(new)_

```csharp
public interface IStockTransactionService
{
  StockTransaction? ParseMessage( string text );
  Task<StockTransaction> CreateAsync( StockTransaction tx );
  Task<List<StockTransaction>> GetAllAsync( string? symbol = null );
  Task<StockTransaction?> GetByIdAsync( long id );
  Task UpdateAsync( StockTransaction tx );
  Task SoftDeleteAsync( long id );
}
```

### `Fiolyn/Services/External/Stock/StockTransactionService.cs` _(new)_

- Inject `IRepository<StockTransaction>` and `ILogger<StockTransactionService>`
- `ParseMessage` — parses the Telegram message text format (see §5)
- CRUD methods delegate to `IRepository<StockTransaction>`

---

## 4. Controller

### `Fiolyn/Controllers/StockTransactionController.cs` _(new)_

Namespace: `Fiolyn.Controllers`. Route: `api/stock-transactions`.

| Verb     | Route   | Action                                |
|----------|---------|---------------------------------------|
| `POST`   | `/`     | Create from request body DTO          |
| `GET`    | `/`     | List all (query param: `?symbol=VNM`) |
| `GET`    | `/{id}` | Get by id                             |
| `PUT`    | `/{id}` | Full update                           |
| `DELETE` | `/{id}` | Soft delete                           |

Use a `StockTransactionRequest` record as the POST/PUT body (same fields as entity minus audit/soft-delete fields).

Always return 200 with a result payload (consistent with project style).

---

## 5. Telegram Message Format

Messages starting with `stock ` are routed to `IStockTransactionService.ParseMessage`. Non-stock messages continue to
Notion as before.

**Buy / Sell:**

```
stock buy VNM 100 45000 4501500 [broker:VPS] [date:2026-04-14] [notes:bought at support]
stock sell AAPL 10 150.5 1500 [broker:Fidelity] [notes:profit taking]
```

Positional: `stock {type} {symbol} {qty} {price} {total}` — optional named params follow.

**Dividend:**

```
stock div VNM 500000 [date:2026-04-14] [notes:Q2 dividend]
```

Positional: `stock div {symbol} {total}` — quantity and price default to `0`.

If `date:` is omitted, `TransactionDate` defaults to UTC now.

### Update `TelegramMessageService` _(modify)_

`Fiolyn/Services/External/Telegram/TelegramMessageService.cs`

- After whitelist check, test if `text.StartsWith( "stock ", StringComparison.OrdinalIgnoreCase )`
- If yes: call `IStockTransactionService.ParseMessage(text)` → save to DB; skip Notion
- If no: continue existing Notion flow

Inject `IStockTransactionService` via primary constructor.

---

## 6. DI Registration

### `Fiolyn/ServiceRegisters/StockTransactionServiceRegister.cs` _(new)_

```csharp
public class StockTransactionServiceRegister : IServiceRegister
{
  public int Order => 1 ;
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<IStockTransactionService, StockTransactionService>() ;
  }
}
```

Auto-discovered — no changes to `Program.cs`.

---

## 7. Tests

**Unit:** `Fiolyn.Tests/Unit/Services/StockTransactionServiceTests.cs`

- `ParseMessage_BuyMessage_ReturnsBuyTransaction`
- `ParseMessage_DividendMessage_ReturnsDividendTransaction`
- `ParseMessage_InvalidFormat_ReturnsNull`
- `CreateAsync_SavesEntityViaRepository`
- `TelegramMessageService_StockMessage_SavesTransactionSkipsNotion`

---

## Files Summary

| Action | File                                                          |
|--------|---------------------------------------------------------------|
| Create | `Fiolyn/Enums/TransactionType.cs`                             |
| Create | `Fiolyn/Entities/StockTransaction.cs`                         |
| Modify | `Fiolyn/Data/AppDbContext.cs`                                 |
| Create | `Fiolyn/Services/External/Stock/IStockTransactionService.cs`  |
| Create | `Fiolyn/Services/External/Stock/StockTransactionService.cs`   |
| Create | `Fiolyn/Controllers/StockTransactionController.cs`            |
| Create | `Fiolyn/ServiceRegisters/StockTransactionServiceRegister.cs`  |
| Modify | `Fiolyn/Services/External/Telegram/TelegramMessageService.cs` |
| Run    | `dotnet ef migrations add AddStockTransaction`                |

---

## Verification

1. `dotnet build Fiolyn/Fiolyn.csproj` — no compile errors
2. `dotnet ef migrations add AddStockTransaction --output-dir Data/Migrations` — migration generated cleanly
3. `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` — all existing 24 tests + new tests pass
4. `mcp__ide__getDiagnostics` — no warnings/errors
5. Manual: POST to `/api/stock-transactions` with a buy body; verify row in DB
6. Manual: Send `stock buy VNM 100 45000 4501500` via Telegram; verify row created, Notion skipped