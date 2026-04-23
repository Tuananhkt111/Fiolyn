using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.StockTransaction ;

/// <summary>
///   Response body for a stock transaction.
/// </summary>
public record StockTransactionResponse(
  long Id,
  string Symbol,
  TransactionType TransactionType,
  long Quantity,
  long Price,
  long TotalAmount,
  DateTime TransactionDate,
  long BrokerAccountId,
  string BrokerAccountName,
  string Notes,
  DateTime Created,
  DateTime LastModified )
{
  public StockTransactionResponse( Entities.StockTransaction t )
    : this(
      t.Id, t.Symbol, t.TransactionType, t.Quantity, t.Price,
      t.TotalAmount, t.TransactionDate, t.BrokerAccountId,
      t.BrokerAccount.Name, t.Notes, t.Created, t.LastModified )
  {
  }
}
