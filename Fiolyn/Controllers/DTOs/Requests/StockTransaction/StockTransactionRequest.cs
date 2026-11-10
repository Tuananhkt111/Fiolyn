using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.StockTransaction ;

/// <summary>
///   Request body for creating or updating a stock transaction. Quantity, price and total are
///   unsigned magnitudes (0 is allowed: a lump-sum dividend has no quantity or price, a per-share one
///   ignores the total); the service applies the sign and rejects a total that comes out zero.
/// </summary>
public record StockTransactionRequest(
  [Required] [MaxLength( 20 )] string Symbol,
  [Required] StockTransactionType StockTransactionType,
  [Required] [Range( 0, long.MaxValue )] long Quantity,
  [Required] [Range( 0, long.MaxValue )] long Price,
  [Required] [Range( 0, long.MaxValue )] long TotalAmount,
  [Required] DateTime Date,
  [Required] long BrokerAccountId,
  long? CategoryId = null,
  long? PayerId = null,
  [MaxLength( 500 )] string Description = "" ) ;
