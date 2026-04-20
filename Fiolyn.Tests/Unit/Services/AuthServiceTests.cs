using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.Extensions.Options ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class AuthServiceTests
{
  private readonly IRepository<AllowedEmail> _allowedEmails = Substitute.For<IRepository<AllowedEmail>>() ;
  private readonly IGoogleTokenVerifier _google = Substitute.For<IGoogleTokenVerifier>() ;
  private readonly InitialAdminSettings _initialAdmin = new() ;
  private readonly IJwtService _jwt = Substitute.For<IJwtService>() ;
  private readonly IRepository<RefreshToken> _refreshTokens = Substitute.For<IRepository<RefreshToken>>() ;
  private readonly IRepository<AppUser> _users = Substitute.For<IRepository<AppUser>>() ;

  private AuthService CreateSut() =>
    new(
      _google, _jwt, _users, _allowedEmails, _refreshTokens,
      Options.Create( _initialAdmin )) ;

  private void SetupAllowedEmails( params string[] emails )
  {
    var list = emails.Select( e => new AllowedEmail
    {
      Email = e
    } ).ToList().BuildMock() ;
    _allowedEmails.QueryActive().Returns( list ) ;
  }

  private void SetupUsers( params AppUser[] users )
  {
    var list = users.ToList().BuildMock() ;
    _users.QueryActive().Returns( list ) ;
  }

  private void SetupRefreshTokens( params RefreshToken[] tokens )
  {
    var list = tokens.ToList().BuildMock() ;
    _refreshTokens.Query().Returns( list ) ;
  }

  private void SetupJwtIssues( string accessToken = "access", string refreshRaw = "refresh-raw", string refreshHash = "refresh-hash" )
  {
    _jwt.CreateAccessToken( Arg.Any<AppUser>() ).Returns( (accessToken, DateTime.UtcNow.AddMinutes( 15 )) ) ;
    _jwt.CreateRefreshToken().Returns( (refreshRaw, refreshHash, DateTime.UtcNow.AddDays( 30 )) ) ;
    _jwt.HashRefreshToken( refreshRaw ).Returns( refreshHash ) ;
  }

  [Fact]
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
  public async Task LoginWithGoogleAsync_FirstLoginOfInitialAdmin_CreatesAdminUser()
  {
    _initialAdmin.Email = "owner@example.com" ;
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "owner@example.com", "sub-1", "Owner", "pic.jpg" ) ) ;
    SetupAllowedEmails( "owner@example.com" ) ;
    SetupUsers() ;
    SetupJwtIssues() ;

    var sut = CreateSut() ;
    var result = await sut.LoginWithGoogleAsync( "token" ) ;

    result.AccessToken.Should().Be( "access" ) ;
    result.RefreshToken.Should().Be( "refresh-raw" ) ;

    await _users.Received( 1 ).AddAsync( Arg.Is<AppUser>( u =>
      u.Email == "owner@example.com"
      && u.GoogleSub == "sub-1"
      && u.IsAdmin ) ) ;

    await _refreshTokens.Received( 1 ).AddAsync( Arg.Is<RefreshToken>( r => r.TokenHash == "refresh-hash" ) ) ;
  }

  [Fact]
  public async Task LoginWithGoogleAsync_NonAdminLogin_CreatesNonAdminUser()
  {
    _initialAdmin.Email = "owner@example.com" ;
    _google.VerifyAsync( "token" )
      .Returns( new GooglePayload( "friend@example.com", "sub-2", "Friend", null ) ) ;
    SetupAllowedEmails( "friend@example.com" ) ;
    SetupUsers() ;
    SetupJwtIssues() ;

    var sut = CreateSut() ;
    await sut.LoginWithGoogleAsync( "token" ) ;

    await _users.Received( 1 ).AddAsync( Arg.Is<AppUser>( u => u.Email == "friend@example.com" && ! u.IsAdmin ) ) ;
  }

  [Fact]
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
    _jwt.HashRefreshToken( "old-raw" ).Returns( "old-hash" ) ;
    _jwt.HashRefreshToken( "refresh-raw" ).Returns( "refresh-hash" ) ;
    _jwt.CreateAccessToken( user ).Returns( ("access", DateTime.UtcNow.AddMinutes( 15 )) ) ;
    _jwt.CreateRefreshToken().Returns( ("refresh-raw", "refresh-hash", DateTime.UtcNow.AddDays( 30 )) ) ;

    var sut = CreateSut() ;
    var result = await sut.RefreshAsync( "old-raw" ) ;

    result.AccessToken.Should().Be( "access" ) ;
    result.RefreshToken.Should().Be( "refresh-raw" ) ;
    existing.RevokedAt.Should().NotBeNull() ;
    existing.ReplacedByTokenHash.Should().Be( "refresh-hash" ) ;
  }

  [Fact]
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
  public async Task RefreshAsync_UnknownToken_Throws()
  {
    SetupRefreshTokens() ;
    _jwt.HashRefreshToken( Arg.Any<string>() ).Returns( "h" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.RefreshAsync( "raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  [Fact]
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
    await sut.LogoutAsync( "raw" ) ;

    token.RevokedAt.Should().NotBeNull() ;
    _refreshTokens.Received( 1 ).Update( token ) ;
  }

  [Fact]
  public async Task LogoutAsync_UnknownToken_Throws()
  {
    SetupRefreshTokens() ;
    _jwt.HashRefreshToken( "raw" ).Returns( "h" ) ;

    var sut = CreateSut() ;

    await sut.Invoking( s => s.LogoutAsync( "raw" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }
}