using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.SpendAccount ;

/// <summary>
///   Request body for adding a user to a shared spend account by email.
/// </summary>
public record AddMemberRequest(
  [Required]
  [EmailAddress]
  [MaxLength( 256 )]
  string Email ) ;
