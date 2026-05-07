using Fiolyn.Controllers.DTOs.Responses.Counterparty ;
using Fiolyn.Controllers.DTOs.Responses.Transaction ;

namespace Fiolyn.Controllers.DTOs.Responses.BorrowingTransaction ;

/// <summary>
///   Response body for a <see cref="Entities.BorrowingTransaction" />.
/// </summary>
public record BorrowingTransactionResponse(
  long Id,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  CounterpartyRefResponse Counterparty,
  DateTime? DueDate,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  DateTime Created,
  DateTime LastModified )
{
  public BorrowingTransactionResponse( Entities.BorrowingTransaction t )
    : this(
      t.Id,
      t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty,
      t.Amount,
      t.Date,
      t.Description,
      new CounterpartyRefResponse( t.Counterparty ),
      t.DueDate,
      t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ),
      t.Payer is null ? null : new TransactionPayerResponse( t.Payer ),
      t.Created,
      t.LastModified )
  {
  }
}
