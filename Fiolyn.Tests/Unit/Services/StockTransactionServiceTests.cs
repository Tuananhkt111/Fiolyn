using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class StockTransactionServiceTests
{
  private readonly Dictionary<long, long> _appliedDeltas = new() ;
  private readonly ISpendAccountBalanceWriter _balanceWriter = Substitute.For<ISpendAccountBalanceWriter>() ;
  private readonly IRepository<BrokerAccount> _brokerRepository = Substitute.For<IRepository<BrokerAccount>>() ;
  private readonly ILogger<StockTransactionService> _logger = Substitute.For<ILogger<StockTransactionService>>() ;
  private readonly IRepository<StockTransaction> _repository = Substitute.For<IRepository<StockTransaction>>() ;
  private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>() ;

  private StockTransactionService CreateSut()
  {
    _unitOfWork.ExecuteInTransactionAsync( Arg.Any<Func<Task>>() )
      .Returns( async ci => await ci.Arg<Func<Task>>()() ) ;

    _balanceWriter.ApplyDeltaAsync( Arg.Any<long>(), Arg.Any<long>() )
      .Returns( ci =>
      {
        var spendId = ci.ArgAt<long>( 0 ) ;
        var delta = ci.ArgAt<long>( 1 ) ;
        _appliedDeltas[ spendId ] = _appliedDeltas.GetValueOrDefault( spendId ) + delta ;
        return Task.CompletedTask ;
      } ) ;

    return new StockTransactionService( _repository, _brokerRepository, _balanceWriter, _unitOfWork, _logger ) ;
  }

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

  private static BrokerAccount BuildBroker( long id, string name, long[] memberIds, long spendAccountId = 1 ) =>
    new()
    {
      Id = id,
      Name = name,
      SpendAccountId = spendAccountId,
      Members = memberIds.Select( m => BuildMember( id, m ) ).ToList()
    } ;

  private void SetupBroker( string name, long id, long[]? memberIds = null, long spendAccountId = 1 )
  {
    var broker = BuildBroker( id, name, memberIds ?? [], spendAccountId ) ;
    var single = new List<BrokerAccount>
    {
      broker
    } ;
    _brokerRepository.QueryActive().Returns( single.BuildMock() ) ;
    _brokerRepository.Query().Returns( single.BuildMock() ) ;
  }

  private void SetupNoBrokers()
  {
    _brokerRepository.QueryActive().Returns( new List<BrokerAccount>().BuildMock() ) ;
    _brokerRepository.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
  }

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
  public async Task CreateAsync_Buy_AppliesNegativeDelta_AndSaves()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Symbol = "VNM",
      TransactionType = TransactionType.Buy,
      Quantity = 100,
      Price = 45000,
      TotalAmount = 4_501_500L,
      TransactionDate = DateTime.UtcNow,
      BrokerAccountId = 1
    } ;

    var result = await sut.CreateAsync( transaction, 42 ) ;

    result.Should().BeSameAs( transaction ) ;
    _appliedDeltas[ 7 ].Should().Be( -4_501_500L ) ;
    await _repository.Received( 1 ).AddAsync( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
    await _unitOfWork.Received( 1 ).ExecuteInTransactionAsync( Arg.Any<Func<Task>>() ) ;
  }

  [Fact]
  public async Task CreateAsync_Sell_AppliesPositiveDelta()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Symbol = "VNM",
      TransactionType = TransactionType.Sell,
      TotalAmount = 2_500_000L,
      BrokerAccountId = 1
    } ;

    await sut.CreateAsync( transaction, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( 2_500_000L ) ;
  }

  [Fact]
  public async Task CreateAsync_Dividend_AppliesPositiveDelta()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Symbol = "VNM",
      TransactionType = TransactionType.Dividend,
      TotalAmount = 500_000L,
      BrokerAccountId = 1
    } ;

    await sut.CreateAsync( transaction, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( 500_000L ) ;
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
    await _balanceWriter.DidNotReceive().ApplyDeltaAsync( Arg.Any<long>(), Arg.Any<long>() ) ;
    await _unitOfWork.DidNotReceive().ExecuteInTransactionAsync( Arg.Any<Func<Task>>() ) ;
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
  public async Task UpdateAsync_SameBroker_AppliesNetDelta()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var original = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = BuildBroker( 1, "VPS", [42], 7 ),
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      original
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    var updated = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_500_000L
    } ;

    await sut.UpdateAsync( updated, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( -500_000L ) ;
    _repository.Received( 1 ).Update( updated ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_SameBroker_NoChange_AppliesZeroDelta()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var original = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = BuildBroker( 1, "VPS", [42], 7 ),
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      original
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    var updated = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;

    await sut.UpdateAsync( updated, 42 ) ;

    // Net 0; ApplyDeltaAsync still called once with delta=0 (the writer short-circuits internally)
    await _balanceWriter.Received( 1 ).ApplyDeltaAsync( 7, 0L ) ;
    _appliedDeltas[ 7 ].Should().Be( 0L ) ;
  }

  [Fact]
  public async Task UpdateAsync_BuyToSell_FlipsBalanceImpact()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    var original = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = BuildBroker( 1, "VPS", [42], 7 ),
      TransactionType = TransactionType.Buy,
      TotalAmount = 2_500_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      original
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    var updated = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      TransactionType = TransactionType.Sell,
      TotalAmount = 2_500_000L
    } ;

    await sut.UpdateAsync( updated, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( 5_000_000L ) ;
  }

  [Fact]
  public async Task UpdateAsync_BrokerChange_ReversesOldSpendAndAppliesNew()
  {
    var oldBroker = new BrokerAccount
    {
      Id = 1,
      Name = "Old",
      SpendAccountId = 7,
      Members = [BuildMember( 1, 42 )]
    } ;
    var newBroker = new BrokerAccount
    {
      Id = 2,
      Name = "New",
      SpendAccountId = 8,
      Members = [BuildMember( 2, 42 )]
    } ;
    var brokers = new List<BrokerAccount>
    {
      oldBroker,
      newBroker
    } ;
    _brokerRepository.QueryActive().Returns( brokers.BuildMock() ) ;
    _brokerRepository.Query().Returns( brokers.BuildMock() ) ;
    var original = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = oldBroker,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      original
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    var updated = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 2,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;

    await sut.UpdateAsync( updated, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( 1_000_000L ) ;
    _appliedDeltas[ 8 ].Should().Be( -1_000_000L ) ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenOriginalNotFound()
  {
    SetupBroker( "VPS", 1L, [42], 7 ) ;
    _repository.QueryActive().Returns( new List<StockTransaction>().BuildMock() ) ;
    var sut = CreateSut() ;
    var updated = new StockTransaction
    {
      Id = 99,
      BrokerAccountId = 1,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1L
    } ;

    var act = async () => await sut.UpdateAsync( updated, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().Update( Arg.Any<StockTransaction>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    SetupBroker( "VPS", 1L, [99], 7 ) ;
    var original = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = BuildBroker( 1, "VPS", [99], 7 ),
      TransactionType = TransactionType.Buy,
      TotalAmount = 1L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      original
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1
    } ;

    var act = async () => await sut.UpdateAsync( transaction, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().Update( Arg.Any<StockTransaction>() ) ;
    await _balanceWriter.DidNotReceive().ApplyDeltaAsync( Arg.Any<long>(), Arg.Any<long>() ) ;
  }


  [Fact]
  public async Task SoftDeleteAsync_ReversesDelta_AndSoftDeletes()
  {
    var broker = BuildBroker( 1, "VPS", [42], 7 ) ;
    _brokerRepository.Query().Returns( new List<BrokerAccount>
    {
      broker
    }.BuildMock() ) ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = broker,
      TransactionType = TransactionType.Buy,
      TotalAmount = 1_000_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      transaction
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5, 42 ) ;

    _appliedDeltas[ 7 ].Should().Be( 1_000_000L ) ;
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
  public async Task AdminSoftDeleteAsync_ReversesDelta_AndSoftDeletes()
  {
    var broker = BuildBroker( 1, "VPS", [], 7 ) ;
    var transaction = new StockTransaction
    {
      Id = 5,
      BrokerAccountId = 1,
      BrokerAccount = broker,
      TransactionType = TransactionType.Sell,
      TotalAmount = 750_000L
    } ;
    _repository.QueryActive().Returns( new List<StockTransaction>
    {
      transaction
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5 ) ;

    _appliedDeltas[ 7 ].Should().Be( -750_000L ) ;
    _repository.Received( 1 ).SoftDelete( transaction ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_ThrowsKeyNotFound_WhenMissing()
  {
    _repository.QueryActive().Returns( new List<StockTransaction>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<StockTransaction>() ) ;
  }
}
