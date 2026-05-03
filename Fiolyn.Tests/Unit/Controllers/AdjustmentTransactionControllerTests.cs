using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.AdjustmentTransaction ;
using Fiolyn.Controllers.DTOs.Responses.AdjustmentTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AdjustmentTransactionControllerTests
{
  private readonly IAdjustmentTransactionService _service =
    Substitute.For<IAdjustmentTransactionService>() ;

  private AdjustmentTransactionController CreateSut( long callerId = 7 )
  {
    var controller = new AdjustmentTransactionController( _service ) ;
    var identity = new ClaimsIdentity(
      [new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )], "TestAuth" ) ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal( identity )
      }
    } ;
    return controller ;
  }

  [Fact]
  public async Task CreateAsync_PassesSignedDeltaThrough()
  {
    AdjustmentTransaction? captured = null ;
    _service.CreateAsync( Arg.Do<AdjustmentTransaction>( t => captured = t ), 7L )
      .Returns( ci => ( AdjustmentTransaction )ci[ 0 ] ) ;
    _service.GetByIdForUserAsync( Arg.Any<long>(), 7L )
      .Returns( ci => new AdjustmentTransaction
      {
        Id = 1,
        SpendAccountId = 5,
        Amount = -50_000L,
        SpendAccount = new SpendAccount
        {
          Id = 5,
          Name = "Wallet"
        }
      } ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new AdjustmentTransactionRequest(
      5,
      null,
      -50_000L,
      DateTime.UtcNow ) ) ;

    captured!.Amount.Should().Be( -50_000L ) ;
    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<AdjustmentTransactionResponse>() ;
  }
}
