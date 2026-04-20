using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.AllowedEmail ;

/// <summary>
///   Request body for POST /api/allowed-emails — adds an email to the SSO whitelist.
/// </summary>
public record AllowedEmailRequest(
  [Required]
  [EmailAddress]
  [MaxLength( 256 )]
  string Email,
  [MaxLength( 500 )] string? Note = null ) ;