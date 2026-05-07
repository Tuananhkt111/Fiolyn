using System.ComponentModel.DataAnnotations ;
using Fiolyn.Controllers.DTOs.Requests.Shared ;

namespace Fiolyn.Controllers.DTOs.Requests.ExpenseTransaction ;

/// <summary>
///   Request body for creating or updating an <see cref="Entities.ExpenseTransaction" />. The
///   controller negates <c>amount</c> before persisting (Expense always reduces balance).
///   <c>amount</c> must be positive — it's the *full* original total. When <c>shares</c> is
///   non-empty the service splits it: each share materialises a paired <c>LendTransaction</c>
///   (counterparty's slice — they owe you) linked to this row via <c>ParentTransactionId</c>;
///   the parent retains the residual. <c>SpendAccount.Balance</c> still moves by the full
///   total.
/// </summary>
public record ExpenseTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null,
  [MaxLength( 20 )] List<TransactionShareRequest>? Shares = null ) ;
