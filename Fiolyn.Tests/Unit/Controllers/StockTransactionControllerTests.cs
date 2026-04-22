using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.StockTransaction ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class StockTransactionControllerTests
{
  private readonly IStockTransactionService _service = Substitute.For<IStockTransactionService>() ;

  private StockTransactionController CreateSut() => new(_service) ;

  private static StockTransactionRequest BuildRequest() =>
    new("vnm", TransactionType.Buy, 100m, 45000m, 4_501_500m,
      new DateTime( 2026, 4, 22, 0, 0, 0, DateTimeKind.Utc ), 3, "notes") ;

  private static StockTransaction BuildEntity( long id = 10, BrokerAccount? broker = null ) =>
    new()
    {
      Id = id,
      Symbol = "VNM",
      TransactionType = TransactionType.Buy,
      Quantity = 100m,
      Price = 45000m,
      TotalAmount = 4_501_500m,
      TransactionDate = new DateTime( 2026, 4, 22, 0, 0, 0, DateTimeKind.Utc ),
      BrokerAccountId = 3,
      BrokerAccount = broker ?? new BrokerAccount
      {
        Id = 3,
        Name = "VPS"
      },
      Notes = "notes"
    } ;

  [Fact]
  public async Task CreateAsync_RefetchesThroughServiceToPopulateBrokerNav()
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

    _service.CreateAsync( Arg.Any<StockTransaction>() ).Returns( created ) ;
    _service.GetByIdAsync( 10 ).Returns( withNav ) ;

    var sut = CreateSut() ;
    var result = await sut.CreateAsync( BuildRequest() ) ;

    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<StockTransactionResponse>().Which ;
    body.BrokerAccountName.Should().Be( "VPS", "response must use refetched nav, not the FK-only post-add entity" ) ;
    await _service.Received( 1 ).GetByIdAsync( 10 ) ;
  }

  [Fact]
  public async Task GetAllAsync_Returns200WithMappedList()
  {
    _service.GetAllAsync().Returns( [BuildEntity( 1 ), BuildEntity( 2 )] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<StockTransactionResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task GetAllAsync_PassesSymbolFilter()
  {
    _service.GetAllAsync( "VNM" ).Returns( [BuildEntity()] ) ;
    var sut = CreateSut() ;

    await sut.GetAllAsync( "VNM" ) ;

    await _service.Received( 1 ).GetAllAsync( "VNM" ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( StockTransaction? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Existing_Returns200()
  {
    _service.GetByIdAsync( 10 ).Returns( BuildEntity() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<StockTransactionResponse>()
      .Which.Id.Should().Be( 10 ) ;
  }

  [Fact]
  public async Task ExistsAsync_True_Returns200()
  {
    _service.ExistsAsync( 10 ).Returns( true ) ;
    var sut = CreateSut() ;

    var result = await sut.ExistsAsync( 10 ) ;

    result.Should().BeOfType<OkResult>() ;
  }

  [Fact]
  public async Task ExistsAsync_False_Returns404()
  {
    _service.ExistsAsync( 10 ).Returns( false ) ;
    var sut = CreateSut() ;

    var result = await sut.ExistsAsync( 10 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( StockTransaction? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, BuildRequest() ) ;

    result.Should().BeOfType<NotFoundResult>() ;
    await _service.DidNotReceive().UpdateAsync( Arg.Any<StockTransaction>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_Existing_AppliesChangesAndReturns200()
  {
    var existing = BuildEntity() ;
    existing.Quantity = 50m ;
    _service.GetByIdAsync( 10 ).Returns( existing ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10, BuildRequest() ) ;

    existing.Quantity.Should().Be( 100m, "request values must be applied to the tracked entity" ) ;
    result.Should().BeOfType<OkObjectResult>() ;
    await _service.Received( 1 ).UpdateAsync( existing ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10 ) ;
  }
}
