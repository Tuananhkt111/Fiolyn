using System.IdentityModel.Tokens.Jwt ;
using Fiolyn.Entities ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.Extensions.Options ;

namespace Fiolyn.Tests.Unit.Services ;

public class JwtServiceTests
{
  private readonly JwtSettings _settings = new()
  {
    Issuer = "Fiolyn",
    Audience = "FiolynMobile",
    SigningKey = "this-is-a-test-signing-key-of-at-least-32-chars",
    AccessTokenExpiryMinutes = 15,
    RefreshTokenExpiryDays = 30
  } ;

  private JwtService CreateSut() => new(Options.Create( _settings )) ;

  [Fact]
  public void CreateAccessToken_IncludesExpectedClaims()
  {
    var sut = CreateSut() ;
    var user = new AppUser
    {
      Id = 42,
      Email = "alice@example.com",
      GoogleSub = "google-sub-123",
      DisplayName = "Alice",
      IsAdmin = true
    } ;

    var (tokenString, expiresAt) = sut.CreateAccessToken( user ) ;

    var handler = new JwtSecurityTokenHandler() ;
    var jwt = handler.ReadJwtToken( tokenString ) ;

    jwt.Issuer.Should().Be( "Fiolyn" ) ;
    jwt.Audiences.Should().Contain( "FiolynMobile" ) ;
    jwt.Claims.Should().Contain( c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "42" ) ;
    jwt.Claims.Should().Contain( c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "alice@example.com" ) ;
    jwt.Claims.Should().Contain( c => c.Type == "isAdmin" && c.Value == "true" ) ;
    jwt.Claims.Should().Contain( c => c.Type == JwtRegisteredClaimNames.Jti ) ;
    expiresAt.Should().BeCloseTo( DateTime.UtcNow.AddMinutes( 15 ), TimeSpan.FromSeconds( 5 ) ) ;
  }

  [Fact]
  public void CreateAccessToken_NonAdmin_SetsIsAdminFalse()
  {
    var sut = CreateSut() ;
    var user = new AppUser
    {
      Id = 1,
      Email = "bob@example.com",
      GoogleSub = "sub",
      IsAdmin = false
    } ;

    var (tokenString, _) = sut.CreateAccessToken( user ) ;
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken( tokenString ) ;

    jwt.Claims.Should().Contain( c => c.Type == "isAdmin" && c.Value == "false" ) ;
  }

  [Fact]
  public void Ctor_MissingSigningKey_Throws()
  {
    Action act = () => _ = new JwtService( Options.Create( new JwtSettings
    {
      SigningKey = ""
    } ) ) ;

    act.Should().Throw<InvalidOperationException>() ;
  }

  [Fact]
  public void Ctor_ShortSigningKey_Throws()
  {
    Action act = () => _ = new JwtService( Options.Create( new JwtSettings
    {
      SigningKey = "too-short"
    } ) ) ;

    act.Should().Throw<InvalidOperationException>() ;
  }

  [Fact]
  public void CreateRefreshToken_RawAndHashAreConsistent()
  {
    var sut = CreateSut() ;

    var (raw, hash, expiresAt) = sut.CreateRefreshToken() ;

    raw.Should().NotBeNullOrWhiteSpace() ;
    hash.Should().NotBeNullOrWhiteSpace() ;
    sut.HashRefreshToken( raw ).Should().Be( hash ) ;
    expiresAt.Should().BeCloseTo( DateTime.UtcNow.AddDays( 30 ), TimeSpan.FromSeconds( 5 ) ) ;
  }

  [Fact]
  public void CreateRefreshToken_EachCallIsUnique()
  {
    var sut = CreateSut() ;

    var first = sut.CreateRefreshToken() ;
    var second = sut.CreateRefreshToken() ;

    first.RawToken.Should().NotBe( second.RawToken ) ;
    first.Hash.Should().NotBe( second.Hash ) ;
  }
}