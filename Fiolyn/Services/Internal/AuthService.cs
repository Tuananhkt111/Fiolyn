using Fiolyn.Controllers.DTOs.Responses.Auth ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Implements the Google SSO login, refresh-with-rotation, and logout flows.
/// </summary>
public class AuthService(
  IGoogleTokenVerifier googleVerifier,
  IJwtService jwtService,
  IRepository<AppUser> userRepository,
  IRepository<AllowedEmail> allowedEmailRepository,
  IRepository<RefreshToken> refreshTokenRepository,
  ILogger<AuthService> logger ) : IAuthService
{
  /// <inheritdoc />
  public async Task<AuthResponse> LoginWithGoogleAsync( string idToken )
  {
    var payload = await googleVerifier.VerifyAsync( idToken ) ;
    var normalizedEmail = AppUser.NormalizeEmail( payload.Email ) ;

    var whitelisted = await allowedEmailRepository.QueryActive()
      .AnyAsync( a => a.Email == normalizedEmail ) ;
    if ( ! whitelisted )
    {
      throw new ForbiddenAccessException( "This email is not authorized to sign in." ) ;
    }

    var user = await userRepository.QueryActive()
      .FirstOrDefaultAsync( u => u.GoogleSub == payload.Sub || u.Email == normalizedEmail ) ;

    if ( user is null )
    {
      user = new AppUser
      {
        Email = normalizedEmail,
        GoogleSub = payload.Sub,
        DisplayName = payload.Name,
        PictureUrl = payload.Picture,
        IsAdmin = false
      } ;
      await userRepository.AddAsync( user ) ;
      logger.LogInformation( "First-time login: created user for {Email} with Google sub {Sub}",
        normalizedEmail, payload.Sub ) ;
    }
    else
    {
      user.Email = normalizedEmail ;
      user.GoogleSub = payload.Sub ;
      user.DisplayName = payload.Name ?? user.DisplayName ;
      user.PictureUrl = payload.Picture ?? user.PictureUrl ;
      userRepository.Update( user ) ;
      logger.LogInformation( "User {UserId} re-authenticated via Google", user.Id ) ;
    }

    user.LastLoginAt = DateTime.UtcNow ;

    var response = await IssueTokensAsync( user ) ;
    await userRepository.SaveAsync() ;
    return response ;
  }

  /// <inheritdoc />
  public async Task<AuthResponse> RefreshAsync( string rawRefreshToken )
  {
    if ( string.IsNullOrWhiteSpace( rawRefreshToken ) )
    {
      throw new UnauthorizedAccessException() ;
    }

    var hash = jwtService.HashRefreshToken( rawRefreshToken ) ;
    var stored = await refreshTokenRepository.Query()
      .Include( r => r.User )
      .FirstOrDefaultAsync( r => r.TokenHash == hash ) ;

    if ( stored is null || stored.ExpiresAt <= DateTime.UtcNow )
    {
      throw new UnauthorizedAccessException() ;
    }

    if ( stored.RevokedAt is not null )
    {
      logger.LogWarning( "Refresh token reuse detected for user {UserId} — revoking rotation chain",
        stored.UserId ) ;
      await RevokeAllActiveTokensAsync( stored.UserId ) ;
      throw new UnauthorizedAccessException() ;
    }

    if ( stored.User.IsDeleted )
    {
      throw new UnauthorizedAccessException() ;
    }

    var newPair = await IssueTokensAsync( stored.User ) ;

    stored.RevokedAt = DateTime.UtcNow ;
    stored.ReplacedByTokenHash = jwtService.HashRefreshToken( newPair.RefreshToken ) ;
    refreshTokenRepository.Update( stored ) ;
    await refreshTokenRepository.SaveAsync() ; // one flush: INSERT new token + UPDATE old token in one transaction

    return newPair ;
  }

  /// <inheritdoc />
  public async Task LogoutAsync( string rawRefreshToken )
  {
    if ( string.IsNullOrWhiteSpace( rawRefreshToken ) )
    {
      throw new UnauthorizedAccessException() ;
    }

    var hash = jwtService.HashRefreshToken( rawRefreshToken ) ;
    var stored = await refreshTokenRepository.Query()
      .FirstOrDefaultAsync( r => r.TokenHash == hash ) ;

    if ( stored is null )
    {
      throw new UnauthorizedAccessException() ;
    }

    if ( stored.RevokedAt is null )
    {
      stored.RevokedAt = DateTime.UtcNow ;
      refreshTokenRepository.Update( stored ) ;
      await refreshTokenRepository.SaveAsync() ;
      logger.LogInformation( "User {UserId} logged out", stored.UserId ) ;
    }
  }

  // Stages the new RefreshToken via AddAsync; persistence is the caller's responsibility
  // so login and refresh can flush all tracked changes in a single transaction.
  private async Task<AuthResponse> IssueTokensAsync( AppUser user )
  {
    var access = jwtService.CreateAccessToken( user ) ;
    var refresh = jwtService.CreateRefreshToken() ;

    await refreshTokenRepository.AddAsync( new RefreshToken
    {
      User = user,
      TokenHash = refresh.Hash,
      ExpiresAt = refresh.ExpiresAt
    } ) ;

    return new AuthResponse( access.Token, refresh.RawToken, access.ExpiresAt ) ;
  }

  private async Task RevokeAllActiveTokensAsync( long userId )
  {
    var active = await refreshTokenRepository.Query()
      .Where( r => r.UserId == userId && r.RevokedAt == null )
      .ToListAsync() ;

    var now = DateTime.UtcNow ;
    foreach ( var token in active )
    {
      token.RevokedAt = now ;
    }

    if ( active.Count > 0 )
    {
      refreshTokenRepository.UpdateRange( active ) ;
      await refreshTokenRepository.SaveAsync() ;
    }
  }
}
