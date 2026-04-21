using Fiolyn.ServiceRegisters.Settings ;
using Google.Apis.Auth ;
using Microsoft.Extensions.Options ;

namespace Fiolyn.Services.External.Google ;

/// <summary>
///   Verifies Google ID tokens using Google's published JWKS and the configured client ID
///   as the expected audience.
/// </summary>
public class GoogleTokenVerifier(
  IOptions<GoogleAuthSettings> settings,
  ILogger<GoogleTokenVerifier> logger ) : IGoogleTokenVerifier
{
  /// <inheritdoc />
  public async Task<GooglePayload> VerifyAsync( string idToken )
  {
    if ( string.IsNullOrWhiteSpace( idToken ) )
    {
      throw new UnauthorizedAccessException( "ID token is empty." ) ;
    }

    var clientId = settings.Value.ClientId ;
    if ( string.IsNullOrWhiteSpace( clientId ) )
    {
      throw new InvalidOperationException( "GoogleAuth:ClientId is not configured." ) ;
    }

    try
    {
      var payload = await GoogleJsonWebSignature.ValidateAsync( idToken, new GoogleJsonWebSignature.ValidationSettings
      {
        Audience = [clientId]
      } ) ;

      if ( string.IsNullOrWhiteSpace( payload.Email ) || ! payload.EmailVerified )
      {
        throw new UnauthorizedAccessException( "Email not present or not verified on Google account." ) ;
      }

      return new GooglePayload( payload.Email, payload.Subject, payload.Name, payload.Picture ) ;
    }
    catch ( InvalidJwtException ex )
    {
      logger.LogWarning( ex, "Invalid Google ID token." ) ;
      throw new UnauthorizedAccessException( "Invalid Google ID token." ) ;
    }
  }
}
