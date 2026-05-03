using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.ExpenseTransaction ;

/// <summary>
///   Request body for creating or updating an <see cref="Entities.ExpenseTransaction" />. The
///   controller negates <c>amount</c> before persisting (Expense always reduces balance).
///   <c>amount</c> must be positive.
/// </summary>
public record ExpenseTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
