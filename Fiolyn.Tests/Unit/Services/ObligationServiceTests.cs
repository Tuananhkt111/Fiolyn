using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Smoke-tests the obligations aggregate. Auto-paired share children live in the standard
///   Lend / Borrow tables and are counted by the same aggregate, so we don't need a separate
///   case for them — exercising real Lend / Borrow rows is sufficient.
/// </summary>
public class ObligationServiceTests
{
  private readonly IRepository<BorrowingTransaction> _borrowingRepo =
    Substitute.For<IRepository<BorrowingTransaction>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<LendTransaction> _lendRepo =
    Substitute.For<IRepository<LendTransaction>>() ;

  private ObligationService CreateSut() => new(_counterpartyRepo, _lendRepo, _borrowingRepo) ;

  [Fact]
  public async Task GetAllForUserAsync_AggregatesLendAndBorrowPerCounterparty()
  {
    var bob = new Counterparty
    {
      Id = 11,
      AppUserId = 7,
      Name = "Bob"
    } ;
    var alice = new Counterparty
    {
      Id = 12,
      AppUserId = 7,
      Name = "Alice"
    } ;
    var stranger = new Counterparty
    {
      Id = 13,
      AppUserId = 99,
      Name = "Stranger"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      bob,
      alice,
      stranger
    }.BuildMock() ) ;

    _lendRepo.Query().Returns( new List<LendTransaction>
    {
      new()
      {
        Id = 1,
        CounterpartyId = 11,
        Counterparty = bob,
        Amount = -200_000L,
        SpendAccountId = 1
      },
      new()
      {
        Id = 2,
        CounterpartyId = 11,
        Counterparty = bob,
        Amount = -50_000L,
        SpendAccountId = 1
      }
    }.BuildMock() ) ;

    _borrowingRepo.Query().Returns( new List<BorrowingTransaction>
    {
      new()
      {
        Id = 3,
        CounterpartyId = 12,
        Counterparty = alice,
        Amount = 80_000L,
        SpendAccountId = 1
      }
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    var result = await sut.GetAllForUserAsync( 7 ) ;

    result.Should().HaveCount( 2 ) ;

    var bobRow = result.Single( r => r.CounterpartyId == 11 ) ;
    bobRow.Receivable.Should().Be( 250_000L ) ;
    bobRow.Payable.Should().Be( 0L ) ;
    bobRow.Net.Should().Be( 250_000L ) ;

    var aliceRow = result.Single( r => r.CounterpartyId == 12 ) ;
    aliceRow.Receivable.Should().Be( 0L ) ;
    aliceRow.Payable.Should().Be( 80_000L ) ;
    aliceRow.Net.Should().Be( -80_000L ) ;

    result.Should().NotContain( r => r.CounterpartyId == 13 ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_OmitsCounterpartiesWithZeroBalances()
  {
    var bob = new Counterparty
    {
      Id = 11,
      AppUserId = 7,
      Name = "Bob"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      bob
    }.BuildMock() ) ;
    _lendRepo.Query().Returns( new List<LendTransaction>().BuildMock() ) ;
    _borrowingRepo.Query().Returns( new List<BorrowingTransaction>().BuildMock() ) ;

    var sut = CreateSut() ;
    var result = await sut.GetAllForUserAsync( 7 ) ;

    result.Should().BeEmpty() ;
  }
}
