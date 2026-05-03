using Fiolyn.Entities ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.Transaction ;

/// <summary>
///   Discriminated polymorphic response for the unified <c>GET /api/transactions</c> list. Carries
///   the common <see cref="Entities.TransactionBase" /> fields plus a <c>kind</c> discriminator and
///   one optional per-leaf detail block (only the matching block is populated).
/// </summary>
public record TransactionResponse(
  long Id,
  TransactionKind Kind,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  TransactionStockDetail? Stock,
  TransactionTransferDetail? Transfer,
  TransactionCounterpartyDetail? Counterparty,
  DateTime Created,
  DateTime LastModified )
{
  public static TransactionResponse FromEntity( TransactionBase t )
  {
    var category = t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ) ;
    var payer = t.Payer is null ? null : new TransactionPayerResponse( t.Payer ) ;
    var spendName = t.SpendAccount?.Name ?? string.Empty ;

    return t switch
    {
      Entities.StockTransaction s => new TransactionResponse(
        s.Id, TransactionKind.Stock, s.SpendAccountId, spendName, s.Amount, s.Date, s.Description,
        category, payer,
        new TransactionStockDetail(
          s.Symbol, s.StockTransactionType, s.Quantity, s.Price, Math.Abs( s.Amount ),
          s.BrokerAccountId, s.BrokerAccount?.Name ?? string.Empty ),
        null,
        null,
        s.Created, s.LastModified ),
      Entities.TransferTransaction tr => new TransactionResponse(
        tr.Id, TransactionKind.Transfer, tr.SpendAccountId, spendName, tr.Amount, tr.Date, tr.Description,
        category, payer,
        null,
        new TransactionTransferDetail(
          tr.DestinationSpendAccountId, tr.DestinationSpendAccount?.Name ?? string.Empty ),
        null,
        tr.Created, tr.LastModified ),
      Entities.LendTransaction l => new TransactionResponse(
        l.Id, TransactionKind.Lend, l.SpendAccountId, spendName, l.Amount, l.Date, l.Description,
        category, payer,
        null,
        null,
        new TransactionCounterpartyDetail( l.CounterpartyName, l.DueDate ),
        l.Created, l.LastModified ),
      Entities.BorrowingTransaction b => new TransactionResponse(
        b.Id, TransactionKind.Borrowing, b.SpendAccountId, spendName, b.Amount, b.Date, b.Description,
        category, payer,
        null,
        null,
        new TransactionCounterpartyDetail( b.CounterpartyName, b.DueDate ),
        b.Created, b.LastModified ),
      Entities.IncomeTransaction => new TransactionResponse(
        t.Id, TransactionKind.Income, t.SpendAccountId, spendName, t.Amount, t.Date, t.Description,
        category, payer, null, null, null, t.Created, t.LastModified ),
      Entities.ExpenseTransaction => new TransactionResponse(
        t.Id, TransactionKind.Expense, t.SpendAccountId, spendName, t.Amount, t.Date, t.Description,
        category, payer, null, null, null, t.Created, t.LastModified ),
      Entities.AdjustmentTransaction => new TransactionResponse(
        t.Id, TransactionKind.Adjustment, t.SpendAccountId, spendName, t.Amount, t.Date, t.Description,
        category, payer, null, null, null, t.Created, t.LastModified ),
      _ => throw new InvalidOperationException( $"Unknown transaction type {t.GetType().Name}" )
    } ;
  }
}

public record TransactionStockDetail(
  string Symbol,
  StockTransactionType StockTransactionType,
  long Quantity,
  long Price,
  long TotalAmount,
  long BrokerAccountId,
  string BrokerAccountName ) ;

public record TransactionTransferDetail(
  long DestinationSpendAccountId,
  string DestinationSpendAccountName ) ;

public record TransactionCounterpartyDetail(
  string CounterpartyName,
  DateTime? DueDate ) ;
