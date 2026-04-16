using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.StockTransaction ;

/// <summary>
///   Response body for a stock transaction.
/// </summary>
public record StockTransactionResponse(
  long Id,
  string Symbol,
  TransactionType TransactionType,
  decimal Quantity,
  decimal Price,
  decimal TotalAmount,
  DateTime TransactionDate,
  long BrokerAccountId,
  string Notes,
  DateTime Created,
  DateTime LastModified )
{
  public StockTransactionResponse( Entities.StockTransaction t )
    : this(
      t.Id, t.Symbol, t.TransactionType, t.Quantity, t.Price,
      t.TotalAmount, t.TransactionDate, t.BrokerAccountId,
      t.Notes, t.Created, t.LastModified )
  {
  }
}