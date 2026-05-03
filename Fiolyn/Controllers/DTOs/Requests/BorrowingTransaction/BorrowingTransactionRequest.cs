using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.BorrowingTransaction ;

/// <summary>
///   Request body for creating or updating a <see cref="Entities.BorrowingTransaction" />.
///   <c>counterpartyName</c> is required; <c>amount</c> is the positive borrowed sum (added to
///   balance).
/// </summary>
public record BorrowingTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [Required] [MaxLength( 100 )] string CounterpartyName,
  DateTime? DueDate = null,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
