using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.SpendAccount ;

/// <summary>
///   Admin request body for creating a spend account on behalf of any user.
/// </summary>
public record AdminSpendAccountCreateRequest(
  [Required] long AppUserId,
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
