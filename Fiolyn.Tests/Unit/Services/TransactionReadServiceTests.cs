using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class TransactionReadServiceTests
{
  private readonly IRepository<TransactionBase> _repository =
    Substitute.For<IRepository<TransactionBase>>() ;

  private TransactionReadService CreateSut() => new(_repository) ;

  private static SpendAccount BuildAccount( long id, long[] memberIds ) =>
    new()
    {
      Id = id,
      Members = memberIds.Select( m => new SpendAccountMember
      {
        SpendAccountId = id,
        AppUserId = m,
        AppUser = new AppUser
        {
          Id = m,
          Email = $"u{m}@x.com",
          GoogleSub = $"g{m}"
        },
        JoinedAt = DateTime.UtcNow
      } ).ToList()
    } ;

  private static BrokerAccount BuildBroker( long id, long[] memberIds, long spendAccountId ) =>
    new()
    {
      Id = id,
      SpendAccountId = spendAccountId,
      Members = memberIds.Select( m => new BrokerAccountMember
      {
        BrokerAccountId = id,
        AppUserId = m,
        AppUser = new AppUser
        {
          Id = m,
          Email = $"u{m}@x.com",
          GoogleSub = $"g{m}"
        },
        JoinedAt = DateTime.UtcNow
      } ).ToList()
    } ;

  [Fact]
  public async Task GetAllForUserAsync_ReturnsOwnedTransactions_OrderedByDateDesc()
  {
    var spend = BuildAccount( 5, [42] ) ;
    var unowned = BuildAccount( 6, [99] ) ;
    var rows = new List<TransactionBase>
    {
      new IncomeTransaction
      {
        Id = 1,
        SpendAccount = spend,
        SpendAccountId = 5,
        Amount = 100,
        Date = new DateTime( 2026, 1, 1, 0, 0, 0, DateTimeKind.Utc )
      },
      new ExpenseTransaction
      {
        Id = 2,
        SpendAccount = spend,
        SpendAccountId = 5,
        Amount = -50,
        Date = new DateTime( 2026, 2, 1, 0, 0, 0, DateTimeKind.Utc )
      },
      new IncomeTransaction
      {
        Id = 3,
        SpendAccount = unowned,
        SpendAccountId = 6,
        Amount = 200,
        Date = new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc )
      }
    } ;
    _repository.Query().Returns( rows.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 42 ) ;

    result.Should().HaveCount( 2 ) ;
    result[ 0 ].Id.Should().Be( 2 ) ;
    result[ 1 ].Id.Should().Be( 1 ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_IncludesStockRowsViaBrokerMembership()
  {
    var spend = BuildAccount( 5, [99] ) ;
    var broker = BuildBroker( 1, [42], 5 ) ;
    var rows = new List<TransactionBase>
    {
      new StockTransaction
      {
        Id = 7,
        SpendAccount = spend,
        SpendAccountId = 5,
        BrokerAccount = broker,
        BrokerAccountId = 1,
        Amount = -1000,
        Date = DateTime.UtcNow,
        Symbol = "VNM",
        Quantity = 1,
        Price = 1000
      }
    } ;
    _repository.Query().Returns( rows.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 42 ) ;

    result.Should().ContainSingle().Which.Should().BeOfType<StockTransaction>() ;
  }

  [Fact]
  public async Task GetAllForUserAsync_IncludesTransfersViaDestinationMembership()
  {
    var source = BuildAccount( 5, [99] ) ;
    var destination = BuildAccount( 6, [42] ) ;
    var rows = new List<TransactionBase>
    {
      new TransferTransaction
      {
        Id = 9,
        SpendAccount = source,
        SpendAccountId = 5,
        DestinationSpendAccount = destination,
        DestinationSpendAccountId = 6,
        Amount = 500,
        Date = DateTime.UtcNow
      }
    } ;
    _repository.Query().Returns( rows.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 42 ) ;

    result.Should().ContainSingle().Which.Should().BeOfType<TransferTransaction>() ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserId_IncludesTransfersViaDestinationMembership()
  {
    var source = BuildAccount( 5, [99] ) ;
    var destination = BuildAccount( 6, [42] ) ;
    var rows = new List<TransactionBase>
    {
      new TransferTransaction
      {
        Id = 9,
        SpendAccount = source,
        SpendAccountId = 5,
        DestinationSpendAccount = destination,
        DestinationSpendAccountId = 6,
        Amount = 500,
        Date = DateTime.UtcNow
      }
    } ;
    _repository.Query().Returns( rows.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 42 ) ;

    result.Should().ContainSingle().Which.Should().BeOfType<TransferTransaction>() ;
  }
}
