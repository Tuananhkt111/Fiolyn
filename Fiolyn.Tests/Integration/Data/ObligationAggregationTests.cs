using Fiolyn.Controllers.DTOs.Responses.Obligation ;
using Fiolyn.Data ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Data ;

/// <summary>
///   US-23: Net = Receivable - Payable per counterparty, over a real context. Lend rows are stored
///   negative and borrow rows positive; expected values below are hand-worked magnitudes.
/// </summary>
public class ObligationAggregationTests
{
  private const long Caller = 7 ;
  private const long Other = 8 ;

  private readonly AppDbContext _ctx = new(
    new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase( Guid.NewGuid().ToString() ).Options ) ;

  private readonly SpendAccount _spend = new() { Name = "Cash", Type = SpendAccountType.Cash } ;

  public ObligationAggregationTests()
  {
    _ctx.AppUser.Add( new AppUser { Id = Caller, Email = "u@example.com", GoogleSub = "s7" } ) ;
    _ctx.AppUser.Add( new AppUser { Id = Other, Email = "o@example.com", GoogleSub = "s8" } ) ;
    _ctx.SpendAccount.Add( _spend ) ;
  }

  private Counterparty AddCounterparty( string name, long userId = Caller )
  {
    var c = new Counterparty { AppUserId = userId, Name = name } ;
    _ctx.Counterparty.Add( c ) ;
    return c ;
  }

  private void AddLend( Counterparty c, long storedAmount, bool deleted = false ) =>
    _ctx.LendTransaction.Add( new LendTransaction
    {
      SpendAccount = _spend,
      Counterparty = c,
      Amount = storedAmount,
      Date = DateTime.UtcNow,
      IsDeleted = deleted
    } ) ;

  private void AddBorrow( Counterparty c, long storedAmount ) =>
    _ctx.BorrowingTransaction.Add( new BorrowingTransaction
    {
      SpendAccount = _spend,
      Counterparty = c,
      Amount = storedAmount,
      Date = DateTime.UtcNow
    } ) ;

  private async Task<IReadOnlyList<ObligationResponse>> RunAsync()
  {
    await _ctx.SaveChangesAsync() ;
    var sut = new ObligationService(
      new Repository<Counterparty>( _ctx ),
      new Repository<LendTransaction>( _ctx ),
      new Repository<BorrowingTransaction>( _ctx ) ) ;
    var page = await sut.GetAllForUserAsync( Caller, new PaginationQuery() ) ;
    return page.Items ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_LendExceedsBorrow_SumsEachSideAndNetIsPositive()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -300_000 ) ;
    AddLend( bob, -200_000 ) ; // Receivable 500_000
    AddBorrow( bob, 120_000 ) ;
    AddBorrow( bob, 80_000 ) ; // Payable 200_000

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.Receivable.Should().Be( 500_000 ) ;
    row.Payable.Should().Be( 200_000 ) ;
    row.Net.Should().Be( 300_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_BorrowExceedsLend_SumsEachSideAndNetIsNegative()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -100_000 ) ;
    AddLend( bob, -50_000 ) ; // Receivable 150_000
    AddBorrow( bob, 250_000 ) ;
    AddBorrow( bob, 150_000 ) ; // Payable 400_000

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.Receivable.Should().Be( 150_000 ) ;
    row.Payable.Should().Be( 400_000 ) ;
    row.Net.Should().Be( -250_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_LendEqualsBorrow_StillListedWithNetZero()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -100_000 ) ;
    AddLend( bob, -200_000 ) ; // Receivable 300_000
    AddBorrow( bob, 150_000 ) ;
    AddBorrow( bob, 150_000 ) ; // Payable 300_000

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.CounterpartyName.Should().Be( "Bob" ) ;
    row.Receivable.Should().Be( 300_000 ) ;
    row.Payable.Should().Be( 300_000 ) ;
    row.Net.Should().Be( 0 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_CounterpartyWithNoTransactions_IsNotListed()
  {
    AddCounterparty( "Idle" ) ;
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -40_000 ) ;

    var items = await RunAsync() ;

    items.Select( i => i.CounterpartyName ).Should().Equal( "Bob" ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_LendOnly_ListedWithPayableZeroAndNetEqualToReceivable()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -70_000 ) ;
    AddLend( bob, -30_000 ) ;

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.Receivable.Should().Be( 100_000 ) ;
    row.Payable.Should().Be( 0 ) ;
    row.Net.Should().Be( 100_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_BorrowOnly_ListedWithReceivableZeroAndNetNegativePayable()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddBorrow( bob, 60_000 ) ;
    AddBorrow( bob, 25_000 ) ;

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.Receivable.Should().Be( 0 ) ;
    row.Payable.Should().Be( 85_000 ) ;
    row.Net.Should().Be( -85_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_OtherUsersCounterpartiesAndSameNamedContact_AreExcluded()
  {
    var mine = AddCounterparty( "Bob" ) ;
    AddLend( mine, -100_000 ) ;
    AddBorrow( mine, 30_000 ) ;

    var theirBob = AddCounterparty( "Bob", Other ) ;
    AddLend( theirBob, -900_000 ) ;
    AddBorrow( theirBob, 700_000 ) ;
    var theirCarol = AddCounterparty( "Carol", Other ) ;
    AddLend( theirCarol, -5_000 ) ;

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.CounterpartyId.Should().Be( mine.Id ) ;
    row.Receivable.Should().Be( 100_000 ) ;
    row.Payable.Should().Be( 30_000 ) ;
    row.Net.Should().Be( 70_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-23" )]
  public async Task GetAllForUserAsync_SoftDeletedLend_DoesNotCount()
  {
    var bob = AddCounterparty( "Bob" ) ;
    AddLend( bob, -100_000 ) ;
    AddLend( bob, -999_000, deleted: true ) ;
    var carol = AddCounterparty( "Carol" ) ;
    AddLend( carol, -50_000, deleted: true ) ; // only a deleted row -> both sums 0 -> not listed

    var items = await RunAsync() ;

    var row = items.Should().ContainSingle().Subject ;
    row.CounterpartyName.Should().Be( "Bob" ) ;
    row.Receivable.Should().Be( 100_000 ) ;
    row.Net.Should().Be( 100_000 ) ;
  }
}
