using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class AdjustmentTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<AdjustmentTransaction> _repository =
    Substitute.For<IRepository<AdjustmentTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public AdjustmentTransactionServiceTests() =>
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;

  private AdjustmentTransactionService CreateSut() => new(_repository, _spendRepo, _categoryRepo) ;

  private static SpendAccount BuildAccount( long id, long[] memberIds, long balance = 0 ) =>
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
  [Trait( "Story", "US-17" )]
  public async Task CreateAsync_AcceptsNegativeDelta()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    await sut.CreateAsync( new AdjustmentTransaction
    {
      SpendAccountId = 5,
      Amount = -50_000L,
      Date = DateTime.UtcNow
    }, 42 ) ;

    account.Balance.Should().Be( 950_000L ) ;
  }

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task CreateAsync_AcceptsPositiveDelta()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    await sut.CreateAsync( new AdjustmentTransaction
    {
      SpendAccountId = 5,
      Amount = 75_000L,
      Date = DateTime.UtcNow
    }, 42 ) ;

    account.Balance.Should().Be( 1_075_000L ) ;
  }
}
