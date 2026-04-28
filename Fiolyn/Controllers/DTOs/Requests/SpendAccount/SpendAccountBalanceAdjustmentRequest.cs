using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.SpendAccount ;

public record SpendAccountBalanceAdjustmentRequest(
  [Required] long Delta ) ;
