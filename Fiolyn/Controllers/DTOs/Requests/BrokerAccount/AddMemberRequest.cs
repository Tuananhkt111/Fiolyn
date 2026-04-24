using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;

/// <summary>
///   Request body for adding a user to a shared broker account by email.
/// </summary>
public record AddMemberRequest(
  [Required]
  [EmailAddress]
  [MaxLength( 256 )]
  string Email ) ;
