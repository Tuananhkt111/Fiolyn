using Fiolyn.Controllers.DTOs.Responses.Transaction ;

namespace Fiolyn.Controllers.DTOs.Responses.LendTransaction ;

/// <summary>
///   Response body for a <see cref="Entities.LendTransaction" />.
/// </summary>
public record LendTransactionResponse(
  long Id,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  string CounterpartyName,
  DateTime? DueDate,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  DateTime Created,
  DateTime LastModified )
{
  public LendTransactionResponse( Entities.LendTransaction t )
    : this(
      t.Id,
      t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty,
      t.Amount,
      t.Date,
      t.Description,
      t.CounterpartyName,
      t.DueDate,
      t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ),
      t.Payer is null ? null : new TransactionPayerResponse( t.Payer ),
      t.Created,
      t.LastModified )
  {
  }
}
