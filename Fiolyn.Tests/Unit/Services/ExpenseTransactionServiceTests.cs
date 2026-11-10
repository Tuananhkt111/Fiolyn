using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.BaseServices ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Spot-checks the Expense → Lend pairing path. The shared base is fully covered by
///   IncomeTransactionServiceTests; this only verifies that Expense produces Lend children
///   (with negative amounts) rather than Borrowing.
/// </summary>
public class ExpenseTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<LendTransaction> _lendRepo =
    Substitute.For<IRepository<LendTransaction>>() ;

  private readonly IRepository<ExpenseTransaction> _repository =
    Substitute.For<IRepository<ExpenseTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public ExpenseTransactionServiceTests()
  {
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
  }

  private ExpenseTransactionService CreateSut() =>
    new(_repository, _spendRepo, _categoryRepo, _counterpartyRepo, _lendRepo) ;

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
  [Trait( "Story", "US-24" )]
  public async Task CreateAsync_WithShare_PairsLendChild_WithNegativeAmount()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;

    var bob = new Counterparty
    {
      Id = 11,
      AppUserId = 42,
      Name = "Bob"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      bob
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    // Controller would have negated request.Amount before calling.
    var tx = new ExpenseTransaction
    {
      SpendAccountId = 5,
      Amount = -100_000L,
      Description = "Dinner",
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", 30 )
    ] ) ;

    tx.Amount.Should().Be( -70_000L ) ;
    tx.Children.Should().ContainSingle() ;
    var child = tx.Children.Single() ;
    child.Should().BeOfType<LendTransaction>() ;
    child.Amount.Should().Be( -30_000L ) ;
    ( ( LendTransaction )child ).Counterparty.Should().Be( bob ) ;
    account.Balance.Should().Be( -100_000L ) ;

    await _lendRepo.Received( 1 ).AddAsync( Arg.Any<LendTransaction>() ) ;
  }

  [Fact]
  [Trait( "Story", "US-24" )]
  public async Task CreateAsync_WithFractionalShare_SplitsByHundredths_NegativeChild()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 12.5% of -1_000_000 → -125_000 child slice (12.5 → 1250 hundredths → 1_000_000 * 1250 / 10000 = 125_000).
    var tx = new ExpenseTransaction
    {
      SpendAccountId = 5,
      Amount = -1_000_000L,
      Description = "Dinner",
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", 12.5 )
    ] ) ;

    var child = tx.Children.Single() ;
    child.Amount.Should().Be( -125_000L ) ;
    tx.Amount.Should().Be( -875_000L ) ;
    account.Balance.Should().Be( -1_000_000L ) ;
  }
}
