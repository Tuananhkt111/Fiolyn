namespace Fiolyn.Services.External.Google ;

/// <summary>
///   Verifies Google-issued ID tokens and extracts identity claims.
/// </summary>
public interface IGoogleTokenVerifier
{
  /// <summary>
  ///   Validates the given Google ID token. Returns the identity payload on success,
  ///   throws <see cref="UnauthorizedAccessException" /> on any validation failure.
  /// </summary>
  /// <param name="idToken">The raw Google ID token JWT from the web client.</param>
  /// <returns>The verified Google identity payload.</returns>
  Task<GooglePayload> VerifyAsync( string idToken ) ;
}

/// <summary>
///   Identity claims extracted from a verified Google ID token.
/// </summary>
public record GooglePayload(
  string Email,
  string Sub,
  string? Name,
  string? Picture ) ;