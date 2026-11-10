using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.Auth ;
using Fiolyn.Controllers.DTOs.Responses.Auth ;
using Fiolyn.Services.Internal.Interfaces ;
using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AuthControllerTests
{
  private readonly IAuthService _authService = Substitute.For<IAuthService>() ;

  private AuthController CreateSut( long callerId = 7 )
  {
    var controller = new AuthController( _authService ) ;
    var identity = new ClaimsIdentity( [new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )], "TestAuth" ) ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal( identity ) }
    } ;
    return controller ;
  }

  [Fact]
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
    await _authService.Received( 1 ).LogoutAsync( "r", 7 ) ;
  }
}
