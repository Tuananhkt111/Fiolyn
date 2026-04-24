using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AdminStockTransactionControllerTests
{
  private readonly IStockTransactionService _service = Substitute.For<IStockTransactionService>() ;

  private AdminStockTransactionController CreateSut() => new(_service) ;

  private static StockTransaction BuildEntity( long id = 10 ) =>
    new()
    {
      Id = id,
      Symbol = "VNM",
      TransactionType = TransactionType.Buy,
      Quantity = 100L,
      Price = 45000L,
      TotalAmount = 4_501_500L,
      TransactionDate = new DateTime( 2026, 4, 22, 0, 0, 0, DateTimeKind.Utc ),
      BrokerAccountId = 3,
      BrokerAccount = new BrokerAccount
      {
        Id = 3,
        Name = "VPS"
      },
      Notes = "notes"
    } ;

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsList()
  {
    _service.GetAllAsync().Returns( [BuildEntity( 1 ), BuildEntity( 2 )] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    await _service.Received( 1 ).GetAllAsync() ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<StockTransactionResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdAndSymbol_PassesFiltersToService()
  {
    _service.GetAllAsync( 42L, "VNM" ).Returns( [BuildEntity( 1 )] ) ;
    var sut = CreateSut() ;

    await sut.GetAllAsync( 42, "VNM" ) ;

    await _service.Received( 1 ).GetAllAsync( 42L, "VNM" ) ;
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

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<StockTransactionResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
    body.BrokerAccountName.Should().Be( "VPS" ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204_AndCallsUnscopedSoftDelete()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10 ) ;
    await _service.DidNotReceive().SoftDeleteAsync( Arg.Any<long>(), Arg.Any<long>() ) ;
  }
}
