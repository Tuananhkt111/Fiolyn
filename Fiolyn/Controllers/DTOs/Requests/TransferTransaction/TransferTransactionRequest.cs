using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.TransferTransaction ;

/// <summary>
///   Request body for creating or updating a <see cref="Entities.TransferTransaction" />. Caller
///   must be a member of both source and destination accounts; the source is debited and the
///   destination credited by <c>amount</c> in the same atomic save.
/// </summary>
public record TransferTransactionRequest(
  [Required] long SpendAccountId,
  [Required] long DestinationSpendAccountId,
  long? CategoryId,
  [Required] [Range( 1, long.MaxValue )] long Amount,
  [Required] DateTime Date,
  [MaxLength( 500 )] string Description = "",
  long? PayerId = null ) ;
