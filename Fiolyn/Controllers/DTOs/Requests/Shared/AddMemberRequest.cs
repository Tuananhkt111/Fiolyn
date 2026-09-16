using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.Shared ;

/// <summary>
///   Request body for adding a user to a shared account by email. Used by both spend accounts and
///   broker accounts — membership works identically for the two, so they share one request shape.
/// </summary>
public record AddMemberRequest(
  [Required]
  [EmailAddress]
  [MaxLength( 256 )]
  string Email ) ;
