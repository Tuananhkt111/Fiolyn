using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.SpendAccount ;

public record SpendAccountUpdateRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type ) ;
