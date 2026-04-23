using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class SpendAccountControllerTests
{
  private readonly ISpendAccountService _service = Substitute.For<ISpendAccountService>() ;

  private SpendAccountController CreateSut( long callerId = 7 )
  {
    var controller = new SpendAccountController( _service ) ;
    var identity = new ClaimsIdentity( [
      new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )
    ], "TestAuth" ) ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal( identity )
      }
    } ;
    return controller ;
  }

  private static SpendAccountRequest BuildRequest( string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash, long balance = 1_500_000L ) =>
    new(name, type, balance) ;

  private static SpendAccount BuildEntity( long id = 10, long userId = 7, string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash, long balance = 1_500_000L ) =>
    new()
    {
      Id = id,
      AppUserId = userId,
      Name = name,
      Type = type,
      Balance = balance
    } ;

  [Fact]
  public async Task CreateAsync_SetsAppUserIdFromClaim_AndReturns201()
  {
    SpendAccount? captured = null ;
    _service.CreateAsync( Arg.Do<SpendAccount>( a => captured = a ) ).Returns( ci => ci.Arg<SpendAccount>() ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.CreateAsync( BuildRequest( "Wallet", SpendAccountType.Cash, 2_000_000L ) ) ;

    captured.Should().NotBeNull() ;
    captured!.AppUserId.Should().Be( 42 ) ;
    captured.Name.Should().Be( "Wallet" ) ;
    captured.Balance.Should().Be( 2_000_000L ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<SpendAccountResponse>().Which ;
    body.AppUserId.Should().Be( 42 ) ;
  }

  [Fact]
  public async Task GetAllAsync_PassesUserIdToService()
  {
    _service.GetAllForUserAsync( 42 ).Returns( [BuildEntity( 1, 42 ), BuildEntity( 2, 42 )] ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.GetAllAsync() ;

    await _service.Received( 1 ).GetAllForUserAsync( 42 ) ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<SpendAccountResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( SpendAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Existing_Returns200()
  {
    _service.GetByIdForUserAsync( 10, 7 ).Returns( BuildEntity() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<SpendAccountResponse>()
      .Which.Id.Should().Be( 10 ) ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( SpendAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, BuildRequest() ) ;

    result.Should().BeOfType<NotFoundResult>() ;
    await _service.DidNotReceive().UpdateAsync( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_Existing_AppliesChangesAndReturns200()
  {
    var existing = BuildEntity( balance: 1_000_000L ) ;
    _service.GetByIdForUserAsync( 10, 7 ).Returns( existing ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10, BuildRequest( "Renamed", SpendAccountType.Credit, 5_000_000L ) ) ;

    existing.Name.Should().Be( "Renamed" ) ;
    existing.Type.Should().Be( SpendAccountType.Credit ) ;
    existing.Balance.Should().Be( 5_000_000L ) ;
    await _service.Received( 1 ).UpdateAsync( existing ) ;
    result.Should().BeOfType<OkObjectResult>() ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut( 42 ) ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10, 42 ) ;
  }
}
