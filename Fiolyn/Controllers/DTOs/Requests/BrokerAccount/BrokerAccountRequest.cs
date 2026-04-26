using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;

/// <summary>
///   Request body for creating or updating a broker account. <c>SpendAccountId</c> links the broker
///   to the spend account whose balance is auto-adjusted by every stock transaction on this broker;
///   the caller must be a member of both accounts.
/// </summary>
public record BrokerAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] long SpendAccountId ) ;
