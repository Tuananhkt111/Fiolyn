namespace Fiolyn.Controllers.DTOs.Responses.Auth ;

/// <summary>
///   Response body for a successful authentication. Returned by login and refresh.
/// </summary>
public record AuthResponse(
  string AccessToken,
  string RefreshToken,
  DateTime ExpiresAt ) ;
