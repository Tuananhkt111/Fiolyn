using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.StockTransaction ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class StockTransactionControllerTests
{
  private readonly IStockTransactionService _service = Substitute.For<IStockTransactionService>() ;

  private StockTransactionController CreateSut( long callerId = 7 )
  {
    var controller = new StockTransactionController( _service ) ;
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

  private static StockTransactionRequest BuildRequest() =>
    new("vnm", StockTransactionType.Buy, 100L, 45000L, 4_501_500L,
      new DateTime( 2026, 4, 22, 0, 0, 0, DateTimeKind.Utc ), 3, Description: "notes") ;

  private static StockTransaction BuildEntity( long id = 10, BrokerAccount? broker = null ) =>
    new()
    {
      Id = id,
      Symbol = "VNM",
      StockTransactionType = StockTransactionType.Buy,
      Quantity = 100L,
      Price = 45000L,
      Amount = -4_501_500L,
      Date = new DateTime( 2026, 4, 22, 0, 0, 0, DateTimeKind.Utc ),
      BrokerAccountId = 3,
      BrokerAccount = broker ?? new BrokerAccount
      {
        Id = 3,
        Name = "VPS"
      },
      Description = "notes"
    } ;

  [Fact]
  public async Task CreateAsync_PassesCallerIdToService_AndRefetchesForBrokerNav()
  {
    var created = BuildEntity( 10, new BrokerAccount
    {
      Id = 3,
      Name = ""
    } ) ;
    var withNav = BuildEntity( 10, new BrokerAccount
    {
      Id = 3,
      Name = "VPS"
    } ) ;

    StockTransaction? captured = null ;
    long capturedUserId = 0 ;
    _service.CreateAsync( Arg.Do<StockTransaction>( t => captured = t ),
        Arg.Do<long>( id => capturedUserId = id ) )
      .Returns( created ) ;
    _service.GetByIdForUserAsync( 10, 42 ).Returns( withNav ) ;

    var sut = CreateSut( 42 ) ;
    var result = await sut.CreateAsync( BuildRequest() ) ;

    captured.Should().NotBeNull() ;
    capturedUserId.Should().Be( 42 ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<StockTransactionResponse>().Which ;
    body.BrokerAccountName.Should().Be( "VPS" ) ;
    await _service.Received( 1 ).GetByIdForUserAsync( 10, 42 ) ;
  }

  [Fact]
  public async Task GetAllAsync_PassesUserIdAndSymbolToService()
  {
    _service.GetAllForUserAsync( 42 ).Returns( [BuildEntity( 1 ), BuildEntity( 2 )] ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.GetAllAsync() ;

    await _service.Received( 1 ).GetAllForUserAsync( 42 ) ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<StockTransactionResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task GetAllAsync_PassesSymbolFilter()
  {
    _service.GetAllForUserAsync( 42, "VNM" ).Returns( [BuildEntity()] ) ;
    var sut = CreateSut( 42 ) ;

    await sut.GetAllAsync( "VNM" ) ;

    await _service.Received( 1 ).GetAllForUserAsync( 42, "VNM" ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( StockTransaction? )null ) ;
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
      .Which.Value.Should().BeOfType<StockTransactionResponse>()
      .Which.Id.Should().Be( 10 ) ;
  }

  [Fact]
  public async Task ExistsAsync_True_Returns200()
  {
    _service.ExistsForUserAsync( 10, 7 ).Returns( true ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 10 ) ).Should().BeOfType<OkResult>() ;
  }

  [Fact]
  public async Task ExistsAsync_False_Returns404()
  {
    _service.ExistsForUserAsync( 10, 7 ).Returns( false ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 10 ) ).Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( StockTransaction? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, BuildRequest() ) ;

    result.Should().BeOfType<NotFoundResult>() ;
    await _service.DidNotReceive().UpdateAsync( Arg.Any<StockTransaction>(), Arg.Any<long>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_BrokerUnchanged_SkipsRefetch()
  {
    var existing = BuildEntity() ;
    existing.Quantity = 50L ;
    _service.GetByIdForUserAsync( 10, 42 ).Returns( existing ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.UpdateAsync( 10, BuildRequest() ) ;

    existing.Quantity.Should().Be( 100L ) ;
    result.Should().BeOfType<OkObjectResult>() ;
    await _service.Received( 1 ).UpdateAsync( existing, 42 ) ;
    await _service.Received( 1 ).GetByIdForUserAsync( 10, 42 ) ;
  }

  [Fact]
  public async Task UpdateAsync_BrokerChanged_RefetchesForFreshNav()
  {
    var existing = BuildEntity() ;
    existing.BrokerAccountId = 5 ;
    _service.GetByIdForUserAsync( 10, 42 ).Returns( existing ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.UpdateAsync( 10, BuildRequest() ) ;

    existing.BrokerAccountId.Should().Be( 3 ) ;
    result.Should().BeOfType<OkObjectResult>() ;
    await _service.Received( 1 ).UpdateAsync( existing, 42 ) ;
    await _service.Received( 2 ).GetByIdForUserAsync( 10, 42 ) ;
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
