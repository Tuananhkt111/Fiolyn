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

  private static AppUser BuildUser( long id, bool isDeleted = false ) =>
    new()
    {
      Id = id,
      Email = $"user{id}@example.com",
      GoogleSub = $"g-{id}",
      IsDeleted = isDeleted
    } ;

  private static BrokerAccountMember BuildMember( long brokerId, long userId, bool userDeleted = false ) =>
    new()
    {
      BrokerAccountId = brokerId,
      AppUserId = userId,
      AppUser = BuildUser( userId, userDeleted ),
      JoinedAt = DateTime.UtcNow
    } ;

  private static BrokerAccount BuildBroker( long id, string name, long[] memberIds ) =>
    new()
    {
      Id = id,
      Name = name,
      Members = memberIds.Select( m => BuildMember( id, m ) ).ToList()
    } ;

  private void SetupBroker( string name, long id, long[]? memberIds = null ) =>
    _brokerRepository.QueryActive().Returns( new List<BrokerAccount>
    {
      BuildBroker( id, name, memberIds ?? [] )
    }.BuildMock() ) ;

  private void SetupNoBrokers() => _brokerRepository.QueryActive().Returns( new List<BrokerAccount>().BuildMock() ) ;

  [Fact]
  public async Task ParseMessageAsync_BuyMessage_ReturnsBuyTransaction()
  {
    SetupBroker( "VPS", 1L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync(
      "stock buy VNM 100 45000 4501500 broker:VPS date:2026-04-14 notes:bought at support" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "VNM" ) ;
    result.TransactionType.Should().Be( TransactionType.Buy ) ;
    result.Quantity.Should().Be( 100L ) ;
    result.Price.Should().Be( 45000L ) ;
    result.TotalAmount.Should().Be( 4501500L ) ;
    result.BrokerAccountId.Should().Be( 1L ) ;
    result.Notes.Should().Be( "bought at support" ) ;
    result.TransactionDate.Should().Be( new DateTime( 2026, 4, 14, 0, 0, 0, DateTimeKind.Utc ) ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_SellMessage_ReturnsSellTransaction()
  {
    SetupBroker( "Fidelity", 2L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock sell AAPL 10 150 1500 broker:Fidelity notes:profit taking" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "AAPL" ) ;
    result.TransactionType.Should().Be( TransactionType.Sell ) ;
    result.Quantity.Should().Be( 10L ) ;
    result.Price.Should().Be( 150L ) ;
    result.TotalAmount.Should().Be( 1500L ) ;
    result.BrokerAccountId.Should().Be( 2L ) ;
    result.Notes.Should().Be( "profit taking" ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_DividendMessage_ReturnsDividendTransaction()
  {
    SetupBroker( "VPS", 1L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock div VNM 500000 broker:VPS date:2026-04-14 notes:Q2 dividend" ) ;

    result.Should().NotBeNull() ;
    result!.Symbol.Should().Be( "VNM" ) ;
    result.TransactionType.Should().Be( TransactionType.Dividend ) ;
    result.Quantity.Should().Be( 0L ) ;
    result.Price.Should().Be( 0L ) ;
    result.TotalAmount.Should().Be( 500000L ) ;
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
  public async Task ParseMessageAsync_BrokerLookup_IsCaseInsensitive()
  {
    SetupBroker( "VPS", 1L ) ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock buy VNM 100 45000 4501500 broker:vps" ) ;

    result.Should().NotBeNull() ;
    result!.BrokerAccountId.Should().Be( 1L ) ;
  }

  [Fact]
  public async Task ParseMessageAsync_BrokerNotFound_ReturnsNull()
  {
    SetupNoBrokers() ;
    var sut = CreateSut() ;

    var result = await sut.ParseMessageAsync( "stock buy VNM 100 45000 4501500 broker:UNKNOWN" ) ;

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
  public async Task CreateAsync_SavesEntity_WhenCallerIsMember()
  {
    SetupBroker( "VPS", 1L, [42] ) ;
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

    var result = await sut.CreateAsync( transaction, 42 ) ;

    result.Should().BeSameAs( transaction ) ;
    await _repository.Received( 1 ).AddAsync( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    SetupBroker( "VPS", 1L, [99] ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      BrokerAccountId = 1,
      Symbol = "VNM"
    } ;

    var act = async () => await sut.CreateAsync( transaction, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _repository.DidNotReceive().AddAsync( Arg.Any<StockTransaction>() ) ;
  }

  [Fact]
  public async Task CreateFromTelegramAsync_SavesEntity_WithoutMembershipCheck()
  {
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Symbol = "VNM",
      BrokerAccountId = 1
    } ;

    var result = await sut.CreateFromTelegramAsync( transaction ) ;

    result.Should().BeSameAs( transaction ) ;
    await _repository.Received( 1 ).AddAsync( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
    _brokerRepository.DidNotReceive().QueryActive() ;
  }

  [Fact]
  public async Task GetAllForUserAsync_FiltersByBrokerMembership()
  {
    var userBroker = BuildBroker( 1, "VPS", [42] ) ;
    var otherBroker = BuildBroker( 2, "TCBS", [99] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 1,
        Symbol = "VNM",
        BrokerAccountId = 1,
        BrokerAccount = userBroker,
        TransactionDate = DateTime.UtcNow
      },
      new()
      {
        Id = 2,
        Symbol = "AAPL",
        BrokerAccountId = 2,
        BrokerAccount = otherBroker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 42 ) ;

    result.Should().ContainSingle().Which.Id.Should().Be( 1 ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_WithSymbolFilter_FiltersSymbolAndMembership()
  {
    var broker = BuildBroker( 1, "VPS", [42] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 1,
        Symbol = "VNM",
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      },
      new()
      {
        Id = 2,
        Symbol = "AAPL",
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 42, "vnm" ) ;

    result.Should().ContainSingle().Which.Symbol.Should().Be( "VNM" ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsTransaction_WhenCallerIsMember()
  {
    var broker = BuildBroker( 1, "VPS", [42] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 5,
        Symbol = "VNM",
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 42 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsNull_WhenCallerIsNotMember()
  {
    var broker = BuildBroker( 1, "VPS", [99] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 5,
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 42 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task ExistsForUserAsync_True_WhenCallerIsMember()
  {
    var broker = BuildBroker( 1, "VPS", [42] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 5,
        BrokerAccountId = 1,
        BrokerAccount = broker
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsForUserAsync( 5, 42 ) ).Should().BeTrue() ;
  }

  [Fact]
  public async Task ExistsForUserAsync_False_WhenCallerIsNotMember()
  {
    var broker = BuildBroker( 1, "VPS", [99] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 5,
        BrokerAccountId = 1,
        BrokerAccount = broker
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsForUserAsync( 5, 42 ) ).Should().BeFalse() ;
  }

  [Fact]
  public async Task UpdateAsync_Updates_WhenCallerIsMember()
  {
    SetupBroker( "VPS", 1L, [42] ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1
    } ;

    await sut.UpdateAsync( transaction, 42 ) ;

    _repository.Received( 1 ).Update( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    SetupBroker( "VPS", 1L, [99] ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1
    } ;

    var act = async () => await sut.UpdateAsync( transaction, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().Update( Arg.Any<StockTransaction>() ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_SoftDeletes_WhenCallerIsMember()
  {
    var broker = BuildBroker( 1, "VPS", [42] ) ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = broker
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      transaction
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5, 42 ) ;

    _repository.Received( 1 ).SoftDelete( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    var broker = BuildBroker( 1, "VPS", [99] ) ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      new()
      {
        Id = 5,
        BrokerAccountId = 1,
        BrokerAccount = broker
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 5, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<StockTransaction>() ) ;
  }

  [Fact]
  public async Task AdminGetAllAsync_NoFilter_ReturnsAll()
  {
    var broker = BuildBroker( 1, "VPS", [42] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 1,
        Symbol = "VNM",
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      },
      new()
      {
        Id = 2,
        Symbol = "AAPL",
        BrokerAccountId = 1,
        BrokerAccount = broker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    result.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task AdminGetAllAsync_WithUserId_FiltersByMembership()
  {
    var userBroker = BuildBroker( 1, "VPS", [42] ) ;
    var otherBroker = BuildBroker( 2, "TCBS", [99] ) ;
    List<StockTransaction> transactions =
    [
      new()
      {
        Id = 1,
        BrokerAccountId = 1,
        BrokerAccount = userBroker,
        TransactionDate = DateTime.UtcNow
      },
      new()
      {
        Id = 2,
        BrokerAccountId = 2,
        BrokerAccount = otherBroker,
        TransactionDate = DateTime.UtcNow
      }
    ] ;
    _repository.QueryActive().Returns( transactions.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 42 ) ;

    result.Should().ContainSingle().Which.Id.Should().Be( 1 ) ;
  }

  [Fact]
  public async Task AdminGetByIdAsync_ReturnsTransaction_RegardlessOfMembership()
  {
    var broker = BuildBroker( 1, "VPS", [99] ) ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      new()
      {
        Id = 5,
        BrokerAccountId = 1,
        BrokerAccount = broker
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 5 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_SoftDeletes_WhenFound()
  {
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1
    } ;
    _repository.GetActiveByIdOrThrowAsync( 5L ).Returns( transaction ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5 ) ;

    _repository.Received( 1 ).SoftDelete( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}
