using Fiolyn.Controllers.DTOs.Responses.Shared ;
using Fiolyn.Controllers.DTOs.Responses.Transaction ;

namespace Fiolyn.Controllers.DTOs.Responses.IncomeTransaction ;

/// <summary>
///   Response body for an <see cref="Entities.IncomeTransaction" />. <see cref="Amount" /> is
///   the caller's portion (after sharing); <see cref="LinkedShares" /> lists the auto-created
///   <see cref="Entities.BorrowingTransaction" /> children (one per share). The original
///   request total = <see cref="Amount" /> + sum of <see cref="LinkedShares" /> amounts.
/// </summary>
public record IncomeTransactionResponse(
  long Id,
  long SpendAccountId,
  string SpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  IReadOnlyList<LinkedShareResponse> LinkedShares,
  DateTime Created,
  DateTime LastModified )
{
  public IncomeTransactionResponse( Entities.IncomeTransaction t )
    : this(
      t.Id,
      t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty,
      t.Amount,
      t.Date,
      t.Description,
      t.Category is null ? null : new TransactionCategoryRefResponse( t.Category ),
      t.Payer is null ? null : new TransactionPayerResponse( t.Payer ),
      LinkedShareResponse.FromParent( t ),
      t.Created,
      t.LastModified )
  {
  }
}
