using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using NSubstitute ;
using NSubstitute.ExceptionExtensions ;

namespace Fiolyn.Tests.Unit.Services ;

public class AuthServiceTests
{
  private readonly IRepository<AllowedEmail> _allowedEmails = Substitute.For<IRepository<AllowedEmail>>() ;
  private readonly IGoogleTokenVerifier _google = Substitute.For<IGoogleTokenVerifier>() ;
  private readonly IJwtService _jwt = Substitute.For<IJwtService>() ;
  private readonly ILogger<AuthService> _logger = Substitute.For<ILogger<AuthService>>() ;
  private readonly IRepository<RefreshToken> _refreshTokens = Substitute.For<IRepository<RefreshToken>>() ;
  private readonly IRepository<AppUser> _users = Substitute.For<IRepository<AppUser>>() ;

  private AuthService CreateSut() => new(_google, _jwt, _users, _allowedEmails, _refreshTokens, _logger) ;

  private void SetupAllowedEmails( params string[] emails )
  {
    var list = emails.Select( e => new AllowedEmail
    {
      Email = e
    } ).ToList().BuildMock() ;
    _allowedEmails.Query().Returns( list ) ;
  }

  private void SetupUsers( params AppUser[] users )
  {
    var list = users.ToList().BuildMock() ;
    _users.Query().Returns( list ) ;
  }

  private void SetupRefreshTokens( params RefreshToken[] tokens )
  {
    var list = tokens.ToList().BuildMock() ;
    _refreshTokens.Query().Returns( list ) ;
  }

  private void SetupJwtIssues( string accessToken = "access", string refreshRaw = "refresh-raw",
    string refreshHash = "refresh-hash" )
  {
    _jwt.CreateAccessToken( Arg.Any<AppUser>() ).Returns( ( accessToken, DateTime.UtcNow.AddMinutes( 15 ) ) ) ;
    _jwt.CreateRefreshToken().Returns( ( refreshRaw, refreshHash, DateTime.UtcNow.AddDays( 30 ) ) ) ;
    _jwt.HashRefreshToken( refreshRaw ).Returns( refreshHash ) ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task LoginWithGoogleAsync_EmailNotWhitelisted_ThrowsForbidden()
  {
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "intruder@example.com", "sub-x", null, null ) ) ;
    SetupAllowedEmails() ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.LoginWithGoogleAsync( "token" ) )
      .Should().ThrowAsync<ForbiddenAccessException>() ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task LoginWithGoogleAsync_FirstLogin_CreatesNonAdminUser()
  {
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "friend@example.com", "sub-2", "Friend", null ) ) ;
    SetupAllowedEmails( "friend@example.com" ) ;
    SetupUsers() ;
    SetupJwtIssues() ;

    var sut = CreateSut() ;
    var result = await sut.LoginWithGoogleAsync( "token" ) ;

    result.AccessToken.Should().Be( "access" ) ;
    result.RefreshToken.Should().Be( "refresh-raw" ) ;

    await _users.Received( 1 ).AddAsync( Arg.Is<AppUser>( u =>
      u.Email == "friend@example.com"
      && u.GoogleSub == "sub-2"
      && ! u.IsAdmin ) ) ;

    await _refreshTokens.Received( 1 ).AddAsync( Arg.Is<RefreshToken>( r => r.TokenHash == "refresh-hash" ) ) ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task LoginWithGoogleAsync_ExistingUser_UpdatesProfileAndLoginTimestamp()
  {
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "alice@example.com", "sub-alice", "Alice New Name", "new-pic.jpg" ) ) ;
    SetupAllowedEmails( "alice@example.com" ) ;
    var existing = new AppUser
    {
      Id = 7,
      Email = "alice@example.com",
      GoogleSub = "sub-alice",
      DisplayName = "Old Name",
      IsAdmin = false
    } ;
    SetupUsers( existing ) ;
    SetupJwtIssues() ;

    var sut = CreateSut() ;
    await sut.LoginWithGoogleAsync( "token" ) ;

    existing.DisplayName.Should().Be( "Alice New Name" ) ;
    existing.PictureUrl.Should().Be( "new-pic.jpg" ) ;
    existing.LastLoginAt.Should().NotBeNull() ;
    _users.Received( 1 ).Update( existing ) ;
    await _users.DidNotReceive().AddAsync( Arg.Any<AppUser>() ) ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_ValidToken_RotatesAndReturnsNewPair()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s"
    } ;
    var existing = new RefreshToken
    {
      Id = 10,
      UserId = 1,
      User = user,
      TokenHash = "old-hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = null
    } ;
    SetupRefreshTokens( existing ) ;
    SetupAllowedEmails( "a@b.com" ) ;
    _jwt.HashRefreshToken( "old-raw" ).Returns( "old-hash" ) ;
    _jwt.HashRefreshToken( "refresh-raw" ).Returns( "refresh-hash" ) ;
    _jwt.CreateAccessToken( user ).Returns( ( "access", DateTime.UtcNow.AddMinutes( 15 ) ) ) ;
    _jwt.CreateRefreshToken().Returns( ( "refresh-raw", "refresh-hash", DateTime.UtcNow.AddDays( 30 ) ) ) ;

    var sut = CreateSut() ;
    var result = await sut.RefreshAsync( "old-raw" ) ;

    result.AccessToken.Should().Be( "access" ) ;
    result.RefreshToken.Should().Be( "refresh-raw" ) ;
    existing.RevokedAt.Should().NotBeNull() ;
    existing.ReplacedByTokenHash.Should().Be( "refresh-hash" ) ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_ConcurrentRotation_LoserGets401_AndDoesNotKeepNewToken()
  {
    // Two concurrent rotations of the same token: the second SaveAsync hits the [ConcurrencyCheck]
    // on RevokedAt and throws DbUpdateConcurrencyException. The service must surface 401, never
    // silently issue a parallel refresh token.
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s"
    } ;
    var existing = new RefreshToken
    {
      Id = 10,
      UserId = 1,
      User = user,
      TokenHash = "old-hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = null
    } ;
    SetupRefreshTokens( existing ) ;
    SetupAllowedEmails( "a@b.com" ) ;
    _jwt.HashRefreshToken( "old-raw" ).Returns( "old-hash" ) ;
    _jwt.HashRefreshToken( "refresh-raw" ).Returns( "refresh-hash" ) ;
    _jwt.CreateAccessToken( user ).Returns( ( "access", DateTime.UtcNow.AddMinutes( 15 ) ) ) ;
    _jwt.CreateRefreshToken().Returns( ( "refresh-raw", "refresh-hash", DateTime.UtcNow.AddDays( 30 ) ) ) ;
    _refreshTokens.SaveAsync().ThrowsAsync( new DbUpdateConcurrencyException() ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "old-raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_RevokedTokenReplay_RevokesChainAndThrows()
  {
    var user = new AppUser
    {
      Id = 2,
      Email = "bob@x.com",
      GoogleSub = "s"
    } ;
    var revoked = new RefreshToken
    {
      Id = 100,
      UserId = 2,
      User = user,
      TokenHash = "replayed-hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = DateTime.UtcNow.AddMinutes( -5 )
    } ;
    var stillActive = new RefreshToken
    {
      Id = 101,
      UserId = 2,
      User = user,
      TokenHash = "active-hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = null
    } ;
    SetupRefreshTokens( revoked, stillActive ) ;
    _jwt.HashRefreshToken( "replay-raw" ).Returns( "replayed-hash" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "replay-raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;

    stillActive.RevokedAt.Should().NotBeNull( "reuse detection must revoke all active tokens for that user" ) ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_SoftDeletedUser_Throws()
  {
    var deletedUser = new AppUser
    {
      Id = 3,
      Email = "ghost@x.com",
      GoogleSub = "s",
      IsDeleted = true
    } ;
    var stored = new RefreshToken
    {
      Id = 50,
      UserId = 3,
      User = deletedUser,
      TokenHash = "hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = null
    } ;
    SetupRefreshTokens( stored ) ;
    _jwt.HashRefreshToken( "raw" ).Returns( "hash" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
    _jwt.DidNotReceive().CreateAccessToken( Arg.Any<AppUser>() ) ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_ExpiredToken_Throws()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s"
    } ;
    var expired = new RefreshToken
    {
      UserId = 1,
      User = user,
      TokenHash = "h",
      ExpiresAt = DateTime.UtcNow.AddMinutes( -1 )
    } ;
    SetupRefreshTokens( expired ) ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_UnknownToken_Throws()
  {
    SetupRefreshTokens() ;
    _jwt.HashRefreshToken( Arg.Any<string>() ).Returns( "h" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  [Fact]
  [Trait( "Story", "US-03" )]
  public async Task LogoutAsync_RevokesToken()
  {
    var token = new RefreshToken
    {
      UserId = 1,
      TokenHash = "h",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 )
    } ;
    SetupRefreshTokens( token ) ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;

    var sut = CreateSut() ;
    await sut.LogoutAsync( "raw", 1 ) ;

    token.RevokedAt.Should().NotBeNull() ;
    _refreshTokens.Received( 1 ).Update( token ) ;
  }

  [Fact]
  [Trait( "Story", "US-03" )]
  public async Task LogoutAsync_AlreadyRevoked_IsNoOp()
  {
    var revoked = new RefreshToken
    {
      UserId = 1,
      TokenHash = "h",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 ),
      RevokedAt = DateTime.UtcNow.AddMinutes( -5 )
    } ;
    var originalRevokedAt = revoked.RevokedAt ;
    SetupRefreshTokens( revoked ) ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;

    var sut = CreateSut() ;
    await sut.LogoutAsync( "raw", 1 ) ;

    revoked.RevokedAt.Should().Be( originalRevokedAt ) ;
    _refreshTokens.DidNotReceive().Update( Arg.Any<RefreshToken>() ) ;
    await _refreshTokens.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  [Trait( "Story", "US-03" )]
  public async Task LogoutAsync_UnknownToken_Throws()
  {
    SetupRefreshTokens() ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.LogoutAsync( "raw", 1 ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  // v1.0 US-04: only whitelisted addresses may use the app. Removing an address must also stop its
  // sessions: a refresh token issued before the removal must not mint new tokens.
  [Fact]
  [Trait( "Story", "US-02" )]
  [Trait( "Story", "US-04" )]
  public async Task RefreshAsync_UserNoLongerWhitelisted_Throws401AndIssuesNoTokens()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "removed@b.com",
      GoogleSub = "s"
    } ;
    var existing = new RefreshToken
    {
      Id = 10,
      UserId = 1,
      User = user,
      TokenHash = "old-hash",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 )
    } ;
    SetupRefreshTokens( existing ) ;
    SetupAllowedEmails( "someone-else@b.com" ) ;
    _jwt.HashRefreshToken( "old-raw" ).Returns( "old-hash" ) ;
    SetupJwtIssues() ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "old-raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
    await _refreshTokens.DidNotReceive().AddAsync( Arg.Any<RefreshToken>() ) ;
    _jwt.DidNotReceive().CreateAccessToken( Arg.Any<AppUser>() ) ;
  }

  // v1.0 US-03: logout revokes the caller's own refresh token. A token belonging to someone else
  // must not be revocable, and the answer must not reveal that the token exists.
  [Fact]
  [Trait( "Story", "US-03" )]
  public async Task LogoutAsync_TokenOwnedByAnotherUser_Throws401AndDoesNotRevoke()
  {
    var othersToken = new RefreshToken
    {
      UserId = 1,
      TokenHash = "h",
      ExpiresAt = DateTime.UtcNow.AddDays( 1 )
    } ;
    SetupRefreshTokens( othersToken ) ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.LogoutAsync( "raw", 2 ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
    othersToken.RevokedAt.Should().BeNull() ;
    await _refreshTokens.DidNotReceive().SaveAsync() ;
  }

  // An admin's soft-delete must hold: a deleted user signing in again with a still-whitelisted email
  // gets 403 — not a 500 from the unique Email/GoogleSub indexes, and not a fresh account.
  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task LoginWithGoogleAsync_SoftDeletedUser_Throws403AndCreatesNothing()
  {
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "gone@example.com", "sub-gone", "Gone", "p.jpg" ) ) ;
    SetupAllowedEmails( "gone@example.com" ) ;
    SetupUsers( new AppUser
    {
      Id = 8,
      Email = "gone@example.com",
      GoogleSub = "sub-gone",
      IsDeleted = true
    } ) ;
    SetupJwtIssues() ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.LoginWithGoogleAsync( "token" ) )
      .Should().ThrowAsync<ForbiddenAccessException>() ;
    await _users.DidNotReceive().AddAsync( Arg.Any<AppUser>() ) ;
    await _refreshTokens.DidNotReceive().AddAsync( Arg.Any<RefreshToken>() ) ;
  }
}
