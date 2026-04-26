using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;

/// <summary>
///   Admin request body for creating a broker account with a specified initial member.
/// </summary>
public record AdminBrokerAccountCreateRequest(
  [Required] long AppUserId,
  [Required] [MaxLength( 100 )] string Name,
  [Required] long SpendAccountId ) ;
