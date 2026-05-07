using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Mirrors <see cref="LendTransactionServiceTests" /> for the borrowing leaf — same shared base,
///   same resolver wiring, sign-flipped amount.
/// </summary>
public class BorrowingTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<BorrowingTransaction> _repository =
    Substitute.For<IRepository<BorrowingTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public BorrowingTransactionServiceTests() =>
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;

  private BorrowingTransactionService CreateSut() => new(_repository, _spendRepo, _categoryRepo, _counterpartyRepo) ;

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
  public async Task CreateAsync_ReusesExistingCounterparty()
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

    var tx = new BorrowingTransaction
    {
      SpendAccountId = 5,
      Amount = 100,
      Date = DateTime.UtcNow
    } ;

    var result = await sut.CreateAsync( tx, 42, "Bob" ) ;

    result.Counterparty.Should().BeSameAs( existing ) ;
    await _counterpartyRepo.DidNotReceive().AddAsync( Arg.Any<Counterparty>() ) ;
  }

  [Fact]
  public async Task CreateAsync_EmptyName_Throws()
  {
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync(
        new BorrowingTransaction
        {
          SpendAccountId = 5,
          Amount = 100
        }, 42, "" ) )
      .Should().ThrowAsync<BadRequestException>() ;
  }
}
