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

    var whitelisted = await allowedEmailRepository.Query()
      .AnyAsync( a => a.Email == normalizedEmail ) ;
    if ( ! whitelisted )
    {
      throw new ForbiddenAccessException( "This email is not authorized to sign in." ) ;
    }

    // Deleted users are looked up too: the unique Email/GoogleSub indexes cover them, and an admin's
    // delete must hold even while the address is still whitelisted (ADR 0024).
    var user = await userRepository.Query()
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync( u => u.GoogleSub == payload.Sub || u.Email == normalizedEmail ) ;
    if ( user is { IsDeleted: true } )
    {
      throw new ForbiddenAccessException( "This account has been deleted." ) ;
    }

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

    // Removing an address from the whitelist ends its sessions at the next refresh, not 30 days later.
    var email = stored.User.Email ;
    if ( ! await allowedEmailRepository.Query().AnyAsync( a => a.Email == email ) )
    {
      logger.LogWarning( "Refresh refused for user {UserId}: email no longer whitelisted", stored.UserId ) ;
      throw new UnauthorizedAccessException() ;
    }

    var newPair = await IssueTokensAsync( stored.User ) ;

    stored.RevokedAt = DateTime.UtcNow ;
    stored.ReplacedByTokenHash = jwtService.HashRefreshToken( newPair.RefreshToken ) ;
    refreshTokenRepository.Update( stored ) ;
    try
    {
      await refreshTokenRepository.SaveAsync() ;
    }
    catch ( DbUpdateConcurrencyException )
    {
      logger.LogWarning( "Concurrent refresh-token rotation race lost for user {UserId}", stored.UserId ) ;
      throw new UnauthorizedAccessException() ;
    }

    return newPair ;
  }

  /// <inheritdoc />
  public async Task LogoutAsync( string rawRefreshToken, long userId )
  {
    if ( string.IsNullOrWhiteSpace( rawRefreshToken ) )
    {
      throw new UnauthorizedAccessException() ;
    }

    var hash = jwtService.HashRefreshToken( rawRefreshToken ) ;
    var stored = await refreshTokenRepository.Query()
      .FirstOrDefaultAsync( r => r.TokenHash == hash ) ;

    // Someone else's token is answered exactly like an unknown one, so the call reveals nothing.
    if ( stored is null || stored.UserId != userId )
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
    var now = DateTime.UtcNow ;
    await refreshTokenRepository.Query()
      .Where( r => r.UserId == userId && r.RevokedAt == null )
      .ExecuteUpdateAsync( setters => setters.SetProperty( r => r.RevokedAt, _ => now ) ) ;
  }
}
