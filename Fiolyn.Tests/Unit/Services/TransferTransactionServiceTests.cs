using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class TransferTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<TransferTransaction> _repository =
    Substitute.For<IRepository<TransferTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public TransferTransactionServiceTests() =>
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;

  private TransferTransactionService CreateSut() => new(_repository, _spendRepo, _categoryRepo) ;

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
  public async Task CreateAsync_DebitsSource_AndCreditsDestination()
  {
    var source = BuildAccount( 5, [42], 1_000_000L ) ;
    var destination = BuildAccount( 6, [42], 0L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      source,
      destination
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( source ) ;
    _spendRepo.GetByIdOrThrowAsync( 6L ).Returns( destination ) ;
    var sut = CreateSut() ;

    await sut.CreateAsync( new TransferTransaction
    {
      SpendAccountId = 5,
      DestinationSpendAccountId = 6,
      Amount = 200_000L,
      Date = DateTime.UtcNow
    }, 42 ) ;

    source.Balance.Should().Be( 800_000L ) ;
    destination.Balance.Should().Be( 200_000L ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsBadRequest_WhenSourceEqualsDestination()
  {
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync( new TransferTransaction
    {
      SpendAccountId = 5,
      DestinationSpendAccountId = 5,
      Amount = 100L
    }, 42 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsKeyNotFound_WhenCallerIsNotMemberOfBoth()
  {
    var source = BuildAccount( 5, [42] ) ;
    var destination = BuildAccount( 6, [99] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      source,
      destination
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync( new TransferTransaction
    {
      SpendAccountId = 5,
      DestinationSpendAccountId = 6,
      Amount = 100L
    }, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ReversesBothLegs()
  {
    var source = BuildAccount( 5, [42], 800_000L ) ;
    var destination = BuildAccount( 6, [42], 200_000L ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( source ) ;
    _spendRepo.GetByIdOrThrowAsync( 6L ).Returns( destination ) ;
    var existing = new TransferTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      DestinationSpendAccountId = 6,
      SpendAccount = source,
      DestinationSpendAccount = destination,
      Amount = 200_000L
    } ;
    _repository.Query().Returns( new List<TransferTransaction>
    {
      existing
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 7, 42 ) ;

    source.Balance.Should().Be( 1_000_000L ) ;
    destination.Balance.Should().Be( 0L ) ;
    _repository.Received( 1 ).SoftDelete( existing ) ;
  }
}
