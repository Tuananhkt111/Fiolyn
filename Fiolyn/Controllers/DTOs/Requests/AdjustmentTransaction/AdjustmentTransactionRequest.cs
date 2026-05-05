using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.AdjustmentTransaction ;

/// <summary>
///   Request body for creating or updating an <see cref="Entities.AdjustmentTransaction" />.
///   Unlike Income/Expense/Lend/Borrowing, <c>amount</c> is <b>signed</b> here and is applied to
///   the spend account balance directly. Use this for one-off corrections (e.g. reconciling
///   against a bank statement).
/// </summary>
public record AdjustmentTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
