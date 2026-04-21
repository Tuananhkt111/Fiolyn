using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.Auth ;

/// <summary>
///   Request body for refresh and logout endpoints — carries the opaque refresh token.
/// </summary>
public record RefreshTokenRequest(
  [Required] string RefreshToken ) ;
