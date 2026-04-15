using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class StockTransactionServiceTests
{
  private readonly IRepository<BrokerAccount> _brokerRepository = Substitute.For<IRepository<BrokerAccount>>() ;
  private readonly ILogger<StockTransactionService> _logger = Substitute.For<ILogger<StockTransactionService>>() ;
  private readonly IRepository<StockTransaction> _repository = Substitute.For<IRepository<StockTransaction>>() ;

  private StockTransactionService CreateSut() => new(_repository, _brokerRepository, _logger) ;

  private void SetupBroker( string name, long id )
  {
    var brokers = new List<BrokerAccount>
    {
      new()
      {
        Id = id,
        Name = name,
        IsDeleted = false
      }
    }.BuildMock() ;
    _brokerRepository.QueryActive().Returns( brokers ) ;
  }

  private void SetupNoBrokers()
  {
    var brokers = new List<BrokerAccount>().BuildMock() ;
    _brokerRepository.QueryActive().Returns( brokers ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_BuyMessage_ReturnsBuyTransaction()
  {
    SetupBroker( "VPS", 1L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock buy VNM 100 45000 4501500 [broker:VPS] [date:2026-04-14] [notes:bought at support]" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "VNM" ) ;
    result.TransactionType.Should().Be( TransactionType.Buy ) ;
    result.Quantity.Should().Be( 100m ) ;
    result.Price.Should().Be( 45000m ) ;
    result.TotalAmount.Should().Be( 4501500m ) ;
    result.BrokerAccountId.Should().Be( 1L ) ;
    result.Notes.Should().Be( "bought at support" ) ;
    result.TransactionDate.Should().Be( new DateTime( 2026, 4, 14, 0, 0, 0, DateTimeKind.Utc ) ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_SellMessage_ReturnsSellTransaction()
  {
    SetupBroker( "Fidelity", 2L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock sell AAPL 10 150.5 1500 [broker:Fidelity] [notes:profit taking]" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "AAPL" ) ;
    result.TransactionType.Should().Be( TransactionType.Sell ) ;
    result.Quantity.Should().Be( 10m ) ;
    result.Price.Should().Be( 150.5m ) ;
    result.TotalAmount.Should().Be( 1500m ) ;
    result.BrokerAccountId.Should().Be( 2L ) ;
    result.Notes.Should().Be( "profit taking" ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_DividendMessage_ReturnsDividendTransaction()
  {
    SetupBroker( "VPS", 1L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock div VNM 500000 [broker:VPS] [date:2026-04-14] [notes:Q2 dividend]" ) ;

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
  public async Task ParseMessageAsync_InvalidFormat_ReturnsNull( string text )
  {
    SetupNoBrokers() ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( text ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task ParseMessageAsync_BrokerNotFound_ReturnsNull()
  {
    SetupNoBrokers() ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock buy VNM 100 45000 4501500 [broker:UNKNOWN]" ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task ParseMessageAsync_NoBrokerParam_ReturnsNull()
  {
    SetupNoBrokers() ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock buy VNM 100 45000 4501500" ) ;

    result.Should().BeNull() ;
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
      BrokerAccountId = 1
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