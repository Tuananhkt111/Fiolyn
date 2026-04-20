using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using System.Security.Cryptography ;
using System.Text ;
using Fiolyn.Constants ;
using Fiolyn.Entities ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.Extensions.Options ;
using Microsoft.IdentityModel.Tokens ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Issues Fiolyn access/refresh tokens. Access tokens are signed JWTs (HS256);
///   refresh tokens are cryptographically random opaque strings whose SHA-256 hash
///   is persisted server-side.
/// </summary>
public class JwtService( IOptions<JwtSettings> settings ) : IJwtService
{
  private const int RefreshTokenBytes = 64 ;

  private static readonly JwtSecurityTokenHandler _tokenHandler = new() ;
  private readonly byte[] _signingKeyBytes = EncodeSigningKey( settings.Value.SigningKey ) ;

  /// <inheritdoc />
  public ( string Token, DateTime ExpiresAt ) CreateAccessToken( AppUser user )
  {
    var settingsValue = settings.Value ;
    var expiresAt = DateTime.UtcNow.AddMinutes( settingsValue.AccessTokenExpiryMinutes ) ;

    var claims = new List<Claim>
    {
      new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
      new(JwtRegisteredClaimNames.Email, user.Email),
      new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
      new(AuthConstants.AdminClaimName, user.IsAdmin ? AuthConstants.AdminClaimTrueValue : "false")
    } ;

    if ( ! string.IsNullOrWhiteSpace( user.DisplayName ) )
    {
      claims.Add( new Claim( JwtRegisteredClaimNames.Name, user.DisplayName ) ) ;
    }

    var credentials = new SigningCredentials(
      new SymmetricSecurityKey( _signingKeyBytes ),
      SecurityAlgorithms.HmacSha256 ) ;

    var token = new JwtSecurityToken(
      settingsValue.Issuer,
      settingsValue.Audience,
      claims,
      DateTime.UtcNow,
      expiresAt,
      credentials ) ;

    return (_tokenHandler.WriteToken( token ), expiresAt) ;
  }

  /// <inheritdoc />
  public ( string RawToken, string Hash, DateTime ExpiresAt ) CreateRefreshToken()
  {
    var bytes = RandomNumberGenerator.GetBytes( RefreshTokenBytes ) ;
    var raw = Convert.ToBase64String( bytes ) ;
    var hash = HashRefreshToken( raw ) ;
    var expiresAt = DateTime.UtcNow.AddDays( settings.Value.RefreshTokenExpiryDays ) ;
    return (raw, hash, expiresAt) ;
  }

  /// <inheritdoc />
  public string HashRefreshToken( string rawToken )
  {
    var bytes = SHA256.HashData( Encoding.UTF8.GetBytes( rawToken ) ) ;
    return Convert.ToHexString( bytes ) ;
  }

  private static byte[] EncodeSigningKey( string signingKey )
  {
    if ( string.IsNullOrWhiteSpace( signingKey ) || signingKey.Length < 32 )
    {
      throw new InvalidOperationException( "Jwt:SigningKey must be set and at least 32 characters long." ) ;
    }

    return Encoding.UTF8.GetBytes( signingKey ) ;
  }
}