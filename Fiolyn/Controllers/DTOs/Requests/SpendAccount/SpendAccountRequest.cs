using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.SpendAccount ;

/// <summary>
///   Request body for creating or updating a spend account.
/// </summary>
public record SpendAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
