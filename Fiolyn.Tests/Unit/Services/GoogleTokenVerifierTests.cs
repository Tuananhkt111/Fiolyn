using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Google ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using Microsoft.Extensions.Options ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class GoogleTokenVerifierTests
{
  private readonly ILogger<GoogleTokenVerifier> _logger = Substitute.For<ILogger<GoogleTokenVerifier>>() ;

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task VerifyAsync_EmptyToken_Throws()
  {
    var sut = new GoogleTokenVerifier( Options.Create( new GoogleAuthSettings
    {
      ClientId = "test-client"
    } ), _logger ) ;

    await sut.Invoking( s => s.VerifyAsync( "" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }

  [Fact]
  public async Task VerifyAsync_NoConfiguredClientId_Throws()
  {
    var sut = new GoogleTokenVerifier( Options.Create( new GoogleAuthSettings
    {
      ClientId = ""
    } ), _logger ) ;

    await sut.Invoking( s => s.VerifyAsync( "not-empty" ) )
      .Should().ThrowAsync<InvalidOperationException>() ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task VerifyAsync_MalformedToken_ThrowsUnauthorized()
  {
    var sut = new GoogleTokenVerifier( Options.Create( new GoogleAuthSettings
    {
      ClientId = "test-client"
    } ), _logger ) ;

    await sut.Invoking( s => s.VerifyAsync( "not.a.valid.jwt" ) )
      .Should().ThrowAsync<UnauthorizedAccessException>() ;
  }
}
