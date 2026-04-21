using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Pure token plumbing: generates Fiolyn access tokens and refresh tokens. No DB access.
/// </summary>
public interface IJwtService
{
  /// <summary>
  ///   Creates a signed JWT access token for the given user.
  /// </summary>
  /// <param name="user">The user to embed in the token claims.</param>
  /// <returns>The compact JWT string and its UTC expiry timestamp.</returns>
  (string Token, DateTime ExpiresAt) CreateAccessToken( AppUser user ) ;

  /// <summary>
  ///   Creates a new cryptographically random refresh token. Returns both the raw token
  ///   (to be sent to the client once) and its SHA-256 hash (to be persisted in the DB).
  /// </summary>
  /// <returns>The raw token, its hash, and the UTC expiry timestamp.</returns>
  (string RawToken, string Hash, DateTime ExpiresAt) CreateRefreshToken() ;

  /// <summary>
  ///   Computes the SHA-256 hash of a raw refresh token for DB lookup.
  /// </summary>
  string HashRefreshToken( string rawToken ) ;
}
