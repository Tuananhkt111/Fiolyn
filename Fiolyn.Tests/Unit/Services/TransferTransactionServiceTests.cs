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
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      source,
      destination
    }.BuildMock() ) ;
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

  // v1.0 US-19 and docs/reference/transactions.md: a transfer requires membership of BOTH accounts.
  // Reads stay visible to a destination-only member; writes must not be possible for them.
  [Fact]
  public async Task SoftDeleteAsync_ThrowsKeyNotFound_WhenCallerIsMemberOfDestinationOnly()
  {
    var source = BuildAccount( 5, [99], 800_000L ) ;
    var destination = BuildAccount( 6, [42], 200_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      source,
      destination
    }.BuildMock() ) ;
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

    var act = () => sut.SoftDeleteAsync( 7, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    source.Balance.Should().Be( 800_000L, "the source account belongs to someone else" ) ;
    destination.Balance.Should().Be( 200_000L ) ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<TransferTransaction>() ) ;
    await _repository.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenCallerIsNotMemberOfTheOriginalSource()
  {
    // Caller 42 belongs to the transfer's destination (6) and to account 7, but not to its source (5).
    // Repointing the source to 7 would reverse the old leg on account 5, which they can't access.
    var original = BuildAccount( 5, [99], 800_000L ) ;
    var destination = BuildAccount( 6, [42], 200_000L ) ;
    var callers = BuildAccount( 7, [42], 500_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      original,
      destination,
      callers
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( original ) ;
    _spendRepo.GetByIdOrThrowAsync( 6L ).Returns( destination ) ;
    _spendRepo.GetByIdOrThrowAsync( 7L ).Returns( callers ) ;
    _repository.Query().Returns( new List<TransferTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        DestinationSpendAccountId = 6,
        Amount = 200_000L
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = () => sut.UpdateAsync( new TransferTransaction
    {
      Id = 7,
      SpendAccountId = 7,
      DestinationSpendAccountId = 6,
      Amount = 200_000L
    }, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    original.Balance.Should().Be( 800_000L ) ;
    callers.Balance.Should().Be( 500_000L ) ;
    await _repository.DidNotReceive().SaveAsync() ;
  }

  // docs/reference/transactions.md: reads stay visible to a member of the destination only, so
  // incoming transfers show up for them. Only writes need membership of both accounts.
  [Fact]
  public async Task GetByIdForUserAsync_DestinationOnlyMember_StillSeesTheTransfer()
  {
    var source = BuildAccount( 5, [99] ) ;
    var destination = BuildAccount( 6, [42] ) ;
    var transfer = new TransferTransaction
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
      transfer
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 7, 42 ) ;

    result.Should().BeSameAs( transfer ) ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenRepointedToAnAccountTheCallerIsNotAMemberOf()
  {
    var source = BuildAccount( 5, [42], 800_000L ) ;
    var destination = BuildAccount( 6, [42], 200_000L ) ;
    var others = BuildAccount( 9, [99], 0L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      source,
      destination,
      others
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( source ) ;
    _spendRepo.GetByIdOrThrowAsync( 6L ).Returns( destination ) ;
    _spendRepo.GetByIdOrThrowAsync( 9L ).Returns( others ) ;
    _repository.Query().Returns( new List<TransferTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        DestinationSpendAccountId = 6,
        Amount = 200_000L
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = () => sut.UpdateAsync( new TransferTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      DestinationSpendAccountId = 9,
      Amount = 200_000L
    }, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    others.Balance.Should().Be( 0L ) ;
    destination.Balance.Should().Be( 200_000L ) ;
    await _repository.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenTransferDoesNotExist()
  {
    _repository.Query().Returns( new List<TransferTransaction>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = () => sut.UpdateAsync( new TransferTransaction
    {
      Id = 404,
      SpendAccountId = 5,
      DestinationSpendAccountId = 6,
      Amount = 1L
    }, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _repository.DidNotReceive().SaveAsync() ;
  }
}
