using Fiolyn.Controllers.DTOs.Responses.Transaction ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.StockTransaction ;

/// <summary>
///   Response body for a stock transaction.
/// </summary>
public record StockTransactionResponse(
  long Id,
  string Symbol,
  StockTransactionType StockTransactionType,
  long Quantity,
  long Price,
  long TotalAmount,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  long BrokerAccountId,
  string BrokerAccountName,
  string Description,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  DateTime Created,
  DateTime LastModified )
{
  public StockTransactionResponse( Entities.StockTransaction t )
    : this(
      t.Id, t.Symbol, t.StockTransactionType, t.Quantity, t.Price,
      Math.Abs( t.Amount ), t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty, t.Amount, t.Date, t.BrokerAccountId,
      t.BrokerAccount?.Name ?? string.Empty, t.Description,
      t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ),
      t.Payer is null ? null : new TransactionPayerResponse( t.Payer ),
      t.Created, t.LastModified )
  {
  }
}
