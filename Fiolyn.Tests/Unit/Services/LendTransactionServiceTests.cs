using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Smoke-tests the lend-specific resolver wiring on top of <see cref="StandardTransactionServiceBase{T}" />.
///   The shared CRUD/balance plumbing is covered by <see cref="IncomeTransactionServiceTests" />.
/// </summary>
public class LendTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<LendTransaction> _repository =
    Substitute.For<IRepository<LendTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public LendTransactionServiceTests() => _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;

  private LendTransactionService CreateSut() => new(_repository, _spendRepo, _categoryRepo, _counterpartyRepo) ;

  private SpendAccount BuildAccount( long id, long[] memberIds, long balance = 0 ) =>
    new()
    {
      Id = id,
      Balance = balance,
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

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task CreateAsync_ReusesExistingCounterparty_CaseInsensitive()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var existing = new Counterparty
    {
      Id = 11,
      AppUserId = 42,
      Name = "Bob"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      existing
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var tx = new LendTransaction
    {
      SpendAccountId = 5,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    var result = await sut.CreateAsync( tx, 42, "BOB" ) ;

    result.Counterparty.Should().BeSameAs( existing ) ;
    await _counterpartyRepo.DidNotReceive().AddAsync( Arg.Any<Counterparty>() ) ;
  }

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task CreateAsync_StagesNewCounterparty_WhenNoMatch()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    Counterparty? added = null ;
    await _counterpartyRepo.AddAsync( Arg.Do<Counterparty>( c => added = c ) ) ;
    var sut = CreateSut() ;

    var tx = new LendTransaction
    {
      SpendAccountId = 5,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    var result = await sut.CreateAsync( tx, 42, "  Alice  " ) ;

    added.Should().NotBeNull() ;
    added!.Name.Should().Be( "Alice" ) ;
    added.AppUserId.Should().Be( 42 ) ;
    result.Counterparty.Should().BeSameAs( added ) ;
  }

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task CreateAsync_OtherUsersCounterpartyName_StagesNewForCaller()
  {
    // Same name "Bob" exists for user 99, but the resolver scopes by AppUserId — user 42 gets
    // a fresh row staged.
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      new()
      {
        Id = 11,
        AppUserId = 99,
        Name = "Bob"
      }
    }.BuildMock() ) ;
    Counterparty? added = null ;
    await _counterpartyRepo.AddAsync( Arg.Do<Counterparty>( c => added = c ) ) ;
    var sut = CreateSut() ;

    var tx = new LendTransaction
    {
      SpendAccountId = 5,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, "Bob" ) ;

    added.Should().NotBeNull() ;
    added!.AppUserId.Should().Be( 42 ) ;
  }

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task CreateAsync_EmptyName_Throws()
  {
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync(
        new LendTransaction
        {
          SpendAccountId = 5,
          Amount = -100
        }, 42, "   " ) )
      .Should().ThrowAsync<BadRequestException>() ;
  }
}
