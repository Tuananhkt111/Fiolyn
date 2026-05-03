using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.LendTransaction ;

/// <summary>
///   Request body for creating or updating a <see cref="Entities.LendTransaction" />. The
///   controller negates <c>amount</c> (lent money = balance out). <c>counterpartyName</c> is
///   required.
/// </summary>
public record LendTransactionRequest(
  [Required] long SpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [Required] [MaxLength( 100 )] string CounterpartyName,
  DateTime? DueDate = null,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
