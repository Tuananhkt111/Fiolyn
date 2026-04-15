using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.External.Stock ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class StockTransactionServiceTests
{
  private readonly ILogger<StockTransactionService> _logger = Substitute.For<ILogger<StockTransactionService>>() ;
  private readonly IRepository<StockTransaction> _repository = Substitute.For<IRepository<StockTransaction>>() ;

  private StockTransactionService CreateSut() => new(_repository, _logger) ;

  [Fact]
  public void ParseMessage_BuyMessage_ReturnsBuyTransaction()
  {
    var sut = CreateSut() ;

    var result = sut.ParseMessage( "stock buy VNM 100 45000 4501500 [broker:VPS] [date:2026-04-14] [notes:bought at support]" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "VNM" ) ;
    result.TransactionType.Should().Be( TransactionType.Buy ) ;
    result.Quantity.Should().Be( 100m ) ;
    result.Price.Should().Be( 45000m ) ;
    result.TotalAmount.Should().Be( 4501500m ) ;
    result.Broker.Should().Be( "VPS" ) ;
    result.Notes.Should().Be( "bought at support" ) ;
    result.TransactionDate.Should().Be( new DateTime( 2026, 4, 14, 0, 0, 0, DateTimeKind.Utc ) ) ;
  }

  [Fact]
  public void ParseMessage_SellMessage_ReturnsSellTransaction()
  {
    var sut = CreateSut() ;

    var result = sut.ParseMessage( "stock sell AAPL 10 150.5 1500 [broker:Fidelity] [notes:profit taking]" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "AAPL" ) ;
    result.TransactionType.Should().Be( TransactionType.Sell ) ;
    result.Quantity.Should().Be( 10m ) ;
    result.Price.Should().Be( 150.5m ) ;
    result.TotalAmount.Should().Be( 1500m ) ;
    result.Broker.Should().Be( "Fidelity" ) ;
    result.Notes.Should().Be( "profit taking" ) ;
  }

  [Fact]
  public void ParseMessage_DividendMessage_ReturnsDividendTransaction()
  {
    var sut = CreateSut() ;

    var result = sut.ParseMessage( "stock div VNM 500000 [date:2026-04-14] [notes:Q2 dividend]" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "VNM" ) ;
    result.TransactionType.Should().Be( TransactionType.Dividend ) ;
    result.Quantity.Should().Be( 0m ) ;
    result.Price.Should().Be( 0m ) ;
    result.TotalAmount.Should().Be( 500000m ) ;
    result.Notes.Should().Be( "Q2 dividend" ) ;
    result.TransactionDate.Should().Be( new DateTime( 2026, 4, 14, 0, 0, 0, DateTimeKind.Utc ) ) ;
  }

  [Theory]
  [InlineData( "" )]
  [InlineData( "not a stock message" )]
  [InlineData( "stock" )]
  [InlineData( "stock buy" )]
  [InlineData( "stock buy VNM" )]
  [InlineData( "stock buy VNM abc 45000 4501500" )]
  [InlineData( "stock unknown VNM 100 45000 4501500" )]
  public void ParseMessage_InvalidFormat_ReturnsNull( string text )
  {
    var sut = CreateSut() ;

    var result = sut.ParseMessage( text ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public void ParseMessage_BuyMessageNoOptionalParams_DefaultsCorrectly()
  {
    var sut = CreateSut() ;

    var result = sut.ParseMessage( "stock buy VNM 100 45000 4501500" ) ;

    result.Should().NotBeNull() ;
    result!.Broker.Should().Be( "" ) ;
    result.Notes.Should().Be( "" ) ;
    result.TransactionDate.Should().BeCloseTo( DateTime.UtcNow, TimeSpan.FromSeconds( 5 ) ) ;
  }

  [Fact]
  public async Task CreateAsync_SavesEntityViaRepository()
  {
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Symbol = "VNM",
      TransactionType = TransactionType.Buy,
      Quantity = 100,
      Price = 45000,
      TotalAmount = 4501500,
      TransactionDate = DateTime.UtcNow,
      Broker = "VPS"
    } ;

    var result = await sut.CreateAsync( transaction ) ;

    result.Should().BeSameAs( transaction ) ;
    await _repository.Received( 1 ).AddAsync( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task GetAllAsync_WithSymbolFilter_FiltersCorrectly()
  {
    var transactions = new List<StockTransaction>
    {
      new()
      {
        Id = 1,
        Symbol = "VNM",
        TransactionDate = DateTime.UtcNow
      },
      new()
      {
        Id = 2,
        Symbol = "AAPL",
        TransactionDate = DateTime.UtcNow
      }
    }.BuildMock() ;
    _repository.QueryActive().Returns( transactions ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( "vnm" ) ;

    result.Should().HaveCount( 1 ) ;
    result[ 0 ].Symbol.Should().Be( "VNM" ) ;
  }
}