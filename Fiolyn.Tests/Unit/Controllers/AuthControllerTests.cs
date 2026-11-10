using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.Auth ;
using Fiolyn.Controllers.DTOs.Responses.Auth ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AuthControllerTests
{
  private readonly IAuthService _authService = Substitute.For<IAuthService>() ;

  private AuthController CreateSut() => new(_authService) ;

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task LoginWithGoogleAsync_Returns200WithTokens()
  {
    var response = new AuthResponse( "access", "refresh", DateTime.UtcNow.AddMinutes( 15 ) ) ;
    _authService.LoginWithGoogleAsync( "idt" ).Returns( response ) ;
    var sut = CreateSut() ;

    var result = await sut.LoginWithGoogleAsync( new GoogleLoginRequest( "idt" ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().Be( response ) ;
  }

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task RefreshAsync_Returns200WithTokens()
  {
    var response = new AuthResponse( "access", "refresh", DateTime.UtcNow.AddMinutes( 15 ) ) ;
    _authService.RefreshAsync( "r" ).Returns( response ) ;
    var sut = CreateSut() ;

    var result = await sut.RefreshAsync( new RefreshTokenRequest( "r" ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().Be( response ) ;
  }

  [Fact]
  public async Task LogoutAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.LogoutAsync( new RefreshTokenRequest( "r" ) ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _authService.Received( 1 ).LogoutAsync( "r" ) ;
  }
}
