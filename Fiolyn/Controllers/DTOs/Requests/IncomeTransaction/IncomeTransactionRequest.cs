using System.ComponentModel.DataAnnotations ;
using Fiolyn.Controllers.DTOs.Requests.Shared ;

namespace Fiolyn.Controllers.DTOs.Requests.IncomeTransaction ;

/// <summary>
///   Request body for creating or updating an <see cref="Entities.IncomeTransaction" />. Income
///   is money in, so the controller persists <c>Amount = +amount</c> directly. <c>amount</c>
///   must be positive — it's the *full* original total. When <c>shares</c> is non-empty the
///   service splits it: each share materialises a paired <c>BorrowingTransaction</c>
///   (counterparty's slice — you owe them) linked to this row via <c>ParentTransactionId</c>;
///   the parent retains the residual. <c>SpendAccount.Balance</c> still moves by the full
///   total.
/// </summary>
public record IncomeTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null,
  [MaxLength( 20 )] List<TransactionShareRequest>? Shares = null ) ;
