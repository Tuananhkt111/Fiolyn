using Fiolyn.Controllers.DTOs.Responses.Transaction ;

namespace Fiolyn.Controllers.DTOs.Responses.TransferTransaction ;

/// <summary>
///   Response body for a <see cref="Entities.TransferTransaction" />. <c>Amount</c> is the
///   positive transferred sum; the source loses it and the destination gains it.
/// </summary>
public record TransferTransactionResponse(
  long Id,
  long SpendAccountId,
  string SpendAccountName,
  long DestinationSpendAccountId,
  string DestinationSpendAccountName,
  long Amount,
  DateTime Date,
  string Description,
  TransactionCategoryRefResponse? Category,
  TransactionPayerResponse? Payer,
  DateTime Created,
  DateTime LastModified )
{
  public TransferTransactionResponse( Entities.TransferTransaction t )
    : this(
      t.Id,
      t.SpendAccountId,
      t.SpendAccount?.Name ?? string.Empty,
      t.DestinationSpendAccountId,
      t.DestinationSpendAccount?.Name ?? string.Empty,
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
