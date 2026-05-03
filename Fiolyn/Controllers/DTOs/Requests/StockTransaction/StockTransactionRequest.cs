using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.StockTransaction ;

/// <summary>
///   Request body for creating or updating a stock transaction.
/// </summary>
public record StockTransactionRequest(
  [Required] [MaxLength( 20 )] string Symbol,
  [Required] StockTransactionType StockTransactionType,
  [Required] long Quantity,
  [Required] long Price,
  [Required] long TotalAmount,
  [Required] DateTime Date,
  [Required] long BrokerAccountId,
  long? CategoryId = null,
  long? PayerId = null,
  [MaxLength( 500 )] string Description = "" ) ;
