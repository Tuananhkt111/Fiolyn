using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;

/// <summary>
///   Request body for creating or updating a broker account.
/// </summary>
public record BrokerAccountRequest(
  [Required] [MaxLength( 100 )] string Name ) ;