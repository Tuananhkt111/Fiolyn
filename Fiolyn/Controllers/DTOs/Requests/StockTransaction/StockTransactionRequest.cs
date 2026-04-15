using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.StockTransaction ;

/// <summary>
///   Request body for creating or updating a stock transaction.
/// </summary>
public record StockTransactionRequest(
  [Required] [MaxLength( 20 )] string Symbol,
  [Required] TransactionType TransactionType,
  [Required] decimal Quantity,
  [Required] decimal Price,
  [Required] decimal TotalAmount,
  [Required] DateTime TransactionDate,
  [Required] long BrokerAccountId,
  [MaxLength( 500 )] string Notes = "" ) ;