using Fiolyn.Controllers.DTOs.Responses.Auth ;
using Fiolyn.Infrastructure.Exceptions ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Orchestrates Google SSO login, token refresh rotation with reuse detection, and logout.
/// </summary>
public interface IAuthService
{
  /// <summary>
  ///   Verifies a Google ID token, enforces the AllowedEmail whitelist, upserts the User,
  ///   and issues a Fiolyn access + refresh token pair.
  /// </summary>
  /// <param name="idToken">Google ID token supplied by the web client.</param>
  /// <exception cref="UnauthorizedAccessException">Token invalid.</exception>
  /// <exception cref="ForbiddenAccessException">Email not whitelisted.</exception>
  Task<AuthResponse> LoginWithGoogleAsync( string idToken ) ;

  /// <summary>
  ///   Rotates a refresh token. On success returns a new access + refresh token pair and
  ///   revokes the presented token. If the presented token is already revoked (reuse),
  ///   the entire rotation chain for that user is revoked and this throws.
  /// </summary>
  /// <exception cref="UnauthorizedAccessException">Token unknown, expired, or reuse detected.</exception>
  Task<AuthResponse> RefreshAsync( string rawRefreshToken ) ;

  /// <summary>
  ///   Revokes a refresh token (logout).
  /// </summary>
  /// <exception cref="UnauthorizedAccessException">Token unknown.</exception>
  Task LogoutAsync( string rawRefreshToken ) ;
}