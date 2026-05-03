using Fiolyn.Controllers.DTOs.Responses.Transaction ;

namespace Fiolyn.Controllers.DTOs.Responses.ExpenseTransaction ;

/// <summary>
///   Response body for an <see cref="Entities.ExpenseTransaction" />.
/// </summary>
public record ExpenseTransactionResponse(
  long Id,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  DateTime Created,
  DateTime LastModified )
{
  public ExpenseTransactionResponse( Entities.ExpenseTransaction t )
    : this(
      t.Id,
      t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty,
      t.Amount,
      t.Date,
      t.Description,
      t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ),
      t.Payer is null ? null : new TransactionPayerResponse( t.Payer ),
      t.Created,
      t.LastModified )
  {
  }
}
