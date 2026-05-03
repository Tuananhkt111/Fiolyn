using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.IncomeTransaction ;

/// <summary>
///   Request body for creating or updating an <see cref="Entities.IncomeTransaction" />. The
///   service negates? No — Income is money in, so the controller persists <c>Amount = +amount</c>
///   directly. <c>amount</c> must be positive.
/// </summary>
public record IncomeTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
