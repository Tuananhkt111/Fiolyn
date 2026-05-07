using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.IncomeTransaction ;
using Fiolyn.Controllers.DTOs.Responses.IncomeTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class IncomeTransactionControllerTests
{
  private readonly IIncomeTransactionService _service = Substitute.For<IIncomeTransactionService>() ;

  private IncomeTransactionController CreateSut( long callerId = 7 )
  {
    var controller = new IncomeTransactionController( _service ) ;
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
  public async Task CreateAsync_DefaultsPayerToCaller_AndReturns201()
  {
    var spend = new SpendAccount
    {
      Id = 5,
      Name = "Wallet"
    } ;
    IncomeTransaction? captured = null ;
    _service.CreateAsync(
        Arg.Do<IncomeTransaction>( t => captured = t ),
        7L,
        Arg.Any<IReadOnlyList<TransactionShareInput>>() )
      .Returns( ci => ( IncomeTransaction )ci[ 0 ] ) ;
    _service.GetByIdForUserAsync( Arg.Any<long>(), 7L )
      .Returns( ci => new IncomeTransaction
      {
        Id = 11,
        SpendAccount = spend,
        SpendAccountId = 5,
        Amount = 100_000L,
        Date = new DateTime( 2026, 4, 29, 0, 0, 0, DateTimeKind.Utc ),
        PayerId = 7
      } ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new IncomeTransactionRequest(
      5,
      null,
      100_000L,
      new DateTime( 2026, 4, 29, 0, 0, 0, DateTimeKind.Utc ) ) ) ;

    captured!.PayerId.Should().Be( 7L ) ;
    captured.Amount.Should().Be( 100_000L ) ;
    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<IncomeTransactionResponse>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Returns404_WhenNotFound()
  {
    _service.GetByIdForUserAsync( 99L, 7L ).Returns( ( IncomeTransaction? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 11 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 11L, 7L ) ;
  }
}
