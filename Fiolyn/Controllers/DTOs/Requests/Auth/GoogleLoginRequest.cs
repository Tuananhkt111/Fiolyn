using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.Auth ;

/// <summary>
///   Request body for POST /api/auth/google — a Google ID token obtained from Google Identity Services (GIS) in the web client.
/// </summary>
public record GoogleLoginRequest(
  [Required] string IdToken ) ;