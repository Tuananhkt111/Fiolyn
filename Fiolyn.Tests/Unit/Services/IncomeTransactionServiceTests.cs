using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Smoke-tests the StandardTransactionServiceBase pipeline through IncomeTransactionService.
///   Expense/Lend/Borrowing/Adjustment share the same base so the same paths apply.
/// </summary>
public class IncomeTransactionServiceTests
{
  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<IncomeTransaction> _repository =
    Substitute.For<IRepository<IncomeTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public IncomeTransactionServiceTests() =>
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;

  private IncomeTransactionService CreateSut() => new(_repository, _spendRepo, _categoryRepo) ;

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
  public async Task CreateAsync_AppliesDelta_AndSaves()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 100_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42 ) ;

    account.Balance.Should().Be( 100_000L ) ;
    await _repository.Received( 1 ).AddAsync( tx ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      BuildAccount( 5, [99] )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 100L
      }, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsBadRequest_WhenCategoryKindMismatch()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _categoryRepo.Query().Returns( new List<TransactionCategory>
    {
      new()
      {
        Id = 9,
        Name = "Groceries",
        Kind = TransactionKind.Expense
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        CategoryId = 9,
        Amount = 100L
      }, 42 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task UpdateAsync_AppliesNetDelta_OnSameAccount()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _repository.Query().Returns( new List<IncomeTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        SpendAccount = account,
        Amount = 100_000L
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.UpdateAsync(
      new IncomeTransaction
      {
        Id = 7,
        SpendAccountId = 5,
        Amount = 250_000L
      }, 42 ) ;

    account.Balance.Should().Be( 150_000L ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ReversesAmount()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var existing = new IncomeTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 200_000L
    } ;
    _repository.Query().Returns( new List<IncomeTransaction>
    {
      existing
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 7, 42 ) ;

    account.Balance.Should().Be( 800_000L ) ;
    _repository.Received( 1 ).SoftDelete( existing ) ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsConflict_WhenBalanceConcurrentlyModified()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _repository.SaveAsync().Returns( Task.FromException( new DbUpdateConcurrencyException() ) ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 100L
      }, 42 ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
  }
}
