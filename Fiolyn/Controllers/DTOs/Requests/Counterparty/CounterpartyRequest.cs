using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.Counterparty ;

/// <summary>
///   Request body for creating or updating a <see cref="Entities.Counterparty" /> via
///   <c>/api/counterparties</c>.
/// </summary>
public record CounterpartyRequest(
  [Required] [MaxLength( 100 )] string Name ) ;
