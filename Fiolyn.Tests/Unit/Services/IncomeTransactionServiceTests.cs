using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.BaseServices ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Smoke-tests the StandardTransactionServiceBase pipeline through IncomeTransactionService,
///   plus the auto-paired Borrowing-child semantics added by ShareableTransactionServiceBase.
///   Adjustment uses the same standard base; Lend/Borrowing share the counterparty base; the
///   paired-child path is unique to Income / Expense and verified here + ExpenseTransactionServiceTests.
/// </summary>
public class IncomeTransactionServiceTests
{
  private readonly IRepository<BorrowingTransaction> _borrowingRepo =
    Substitute.For<IRepository<BorrowingTransaction>>() ;

  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<IncomeTransaction> _repository =
    Substitute.For<IRepository<IncomeTransaction>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public IncomeTransactionServiceTests()
  {
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
  }

  private IncomeTransactionService CreateSut() =>
    new(_repository, _spendRepo, _categoryRepo, _counterpartyRepo, _borrowingRepo) ;

  private static IReadOnlyList<TransactionShareInput> NoShares() => [] ;

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
  public async Task CreateAsync_NoShares_AppliesDelta_AndSaves()
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

    await sut.CreateAsync( tx, 42, NoShares() ) ;

    account.Balance.Should().Be( 100_000L ) ;
    tx.Amount.Should().Be( 100_000L ) ;
    tx.Children.Should().BeEmpty() ;
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
      }, 42, NoShares() ) ;

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
      }, 42, NoShares() ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task CreateAsync_WithShare_PairsBorrowingChild_AndSplitsAmount()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;

    var existingBob = new Counterparty
    {
      Id = 11,
      AppUserId = 42,
      Name = "Bob"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      existingBob
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 1_000_000L,
      Description = "Salary",
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "BOB", 30 )
    ] ) ;

    // Parent gets the residual; full original amount lands on the SpendAccount once.
    tx.Amount.Should().Be( 700_000L ) ;
    tx.Children.Should().HaveCount( 1 ) ;
    var child = tx.Children.Single() ;
    child.Should().BeOfType<BorrowingTransaction>() ;
    child.Amount.Should().Be( 300_000L ) ;
    child.SpendAccountId.Should().Be( 5 ) ;
    child.Description.Should().Contain( "Bob" ).And.Contain( "Salary" ) ;
    ( ( BorrowingTransaction )child ).Counterparty.Should().Be( existingBob ) ;
    account.Balance.Should().Be( 1_000_000L ) ;

    await _borrowingRepo.Received( 1 ).AddAsync( Arg.Any<BorrowingTransaction>() ) ;
    // (no Lend children expected — Income parents only ever spawn Borrowing children.)
  }

  [Fact]
  public async Task CreateAsync_WithFractionalShare_SplitsByHundredthsAndAbsorbsResidual()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 33.33% × 3 on 10_000_000 → each child = 10_000_000 * 3333 / 10000 = 3_333_000;
    // children sum 9_999_000; parent absorbs 1_000.
    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 10_000_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", 33.33 ),
      new TransactionShareInput( "Alice", 33.33 ),
      new TransactionShareInput( "Carol", 33.33 )
    ] ) ;

    tx.Children.Should().HaveCount( 3 ) ;
    tx.Children.Sum( c => c.Amount ).Should().Be( 9_999_000L ) ;
    tx.Amount.Should().Be( 1_000L ) ;
    ( tx.Amount + tx.Children.Sum( c => c.Amount ) ).Should().Be( 10_000_000L ) ;
    account.Balance.Should().Be( 10_000_000L ) ;
  }

  [Fact]
  public async Task CreateAsync_SilentlyRoundsExcessPrecisionToTwoDecimals()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 33.334 rounds *down* to 33.33 — three of them = 99.99 ≤ 100, accepted; behaves
    // identically to the explicit 33.33 case above.
    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 10_000_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", 33.334 ),
      new TransactionShareInput( "Alice", 33.334 ),
      new TransactionShareInput( "Carol", 33.334 )
    ] ) ;

    tx.Children.Sum( c => c.Amount ).Should().Be( 9_999_000L ) ;
    tx.Amount.Should().Be( 1_000L ) ;
  }

  [Fact]
  public async Task CreateAsync_RejectsWhenRoundedSumExceeds100()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 33.336 rounds *up* to 33.34 — three of them = 100.02 > 100, rejected.
    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 10_000_000L
      }, 42, [
        new TransactionShareInput( "Bob", 33.336 ),
        new TransactionShareInput( "Alice", 33.336 ),
        new TransactionShareInput( "Carol", 33.336 )
      ] ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    await _repository.DidNotReceive().AddAsync( Arg.Any<IncomeTransaction>() ) ;
  }

  [Fact]
  public async Task CreateAsync_RejectsBelowMinimumPercentage()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 0.004 rounds to 0.00 → 0 hundredths → below the minimum of 1 hundredth.
    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 10_000_000L
      }, 42, [
        new TransactionShareInput( "Bob", 0.004 )
      ] ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task CreateAsync_RoundingAbsorbedByParent_TotalsReconcile()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;

    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;

    // 33% × 100 = 33 each → children sum 99 → parent absorbs the leftover 1.
    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 100L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", 33 ),
      new TransactionShareInput( "Alice", 33 ),
      new TransactionShareInput( "Carol", 33 )
    ] ) ;

    tx.Children.Sum( c => c.Amount ).Should().Be( 99 ) ;
    tx.Amount.Should().Be( 1L ) ;
    ( tx.Amount + tx.Children.Sum( c => c.Amount ) ).Should().Be( 100L ) ;
    account.Balance.Should().Be( 100L ) ;
  }

  [Fact]
  public async Task CreateAsync_RejectsShareSumOver100()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;

    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 100L
      }, 42, [
        new TransactionShareInput( "Bob", 60 ),
        new TransactionShareInput( "Alice", 50 )
      ] ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    await _repository.DidNotReceive().AddAsync( Arg.Any<IncomeTransaction>() ) ;
    await _borrowingRepo.DidNotReceive().AddAsync( Arg.Any<BorrowingTransaction>() ) ;
  }

  [Fact]
  public async Task CreateAsync_RejectsDuplicateCounterparty_CaseInsensitive()
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;

    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;

    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync(
      new IncomeTransaction
      {
        SpendAccountId = 5,
        Amount = 100L
      }, 42, [
        new TransactionShareInput( "Bob", 30 ),
        new TransactionShareInput( "BOB", 20 )
      ] ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task UpdateAsync_ReplacesLinkedChildren_AndAppliesDeltaForFullTotal()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
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
    var carol = new Counterparty
    {
      Id = 12,
      AppUserId = 42,
      Name = "Carol"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      bob,
      carol
    }.BuildMock() ) ;

    // Existing tracked parent (eager-loaded by GetByIdForUserAsync) — Children = active old
    // child contributing 300k. Controller mutated `Amount` to the new total (2_000_000) before
    // calling UpdateAsync.
    var existingChild = new BorrowingTransaction
    {
      Id = 50,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 300_000L,
      CounterpartyId = 11,
      Counterparty = bob,
      ParentTransactionId = 7
    } ;
    var parent = new IncomeTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 2_000_000L,
      Children = [existingChild]
    } ;

    // Mock the AsNoTracking projection of the *old* DB row separately so the projection sees
    // the pre-mutation Amount (700_000) — in real EF this is what AsNoTracking would
    // re-materialise from the DB. Same separate-instance pattern as UpdateAsync_AppliesNetDelta.
    _repository.Query().Returns( new List<IncomeTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        SpendAccount = account,
        Amount = 700_000L
      }
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    await sut.UpdateAsync( parent, 42, [
      new TransactionShareInput( "Carol", 25 )
    ] ) ;

    // Old child hard-deleted, new Carol child staged.
    _borrowingRepo.Received( 1 ).Delete( existingChild ) ;
    parent.Children.Should().HaveCount( 1 ) ;
    var newChild = parent.Children.Single() ;
    newChild.Should().BeOfType<BorrowingTransaction>() ;
    newChild.Amount.Should().Be( 500_000L ) ;
    ( ( BorrowingTransaction )newChild ).Counterparty.Should().Be( carol ) ;
    parent.Amount.Should().Be( 1_500_000L ) ;

    // Balance: oldTotal = 700_000 (old parent) + 300_000 (old child) = 1_000_000;
    // newTotal = 2_000_000; delta = +1_000_000 onto starting 1_000_000 → 2_000_000.
    account.Balance.Should().Be( 2_000_000L ) ;
  }

  [Fact]
  public async Task UpdateAsync_CrossSpendAccountWithShares_ReversesOldTotalAndAppliesNewTotal()
  {
    var oldAccount = BuildAccount( 5, [42], 1_000_000L ) ;
    var newAccount = BuildAccount( 6, [42], 500_000L ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      oldAccount,
      newAccount
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( oldAccount ) ;
    _spendRepo.GetByIdOrThrowAsync( 6L ).Returns( newAccount ) ;

    var bob = new Counterparty
    {
      Id = 11,
      AppUserId = 42,
      Name = "Bob"
    } ;
    var carol = new Counterparty
    {
      Id = 12,
      AppUserId = 42,
      Name = "Carol"
    } ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>
    {
      bob,
      carol
    }.BuildMock() ) ;

    var existingChild = new BorrowingTransaction
    {
      Id = 50,
      SpendAccountId = 5,
      SpendAccount = oldAccount,
      Amount = 300_000L,
      CounterpartyId = 11,
      Counterparty = bob,
      ParentTransactionId = 7
    } ;
    var parent = new IncomeTransaction
    {
      Id = 7,
      SpendAccountId = 6, // Controller mutated to the new SpendAccount.
      SpendAccount = newAccount,
      Amount = 2_000_000L, // Controller mutated to the new total.
      Children = [existingChild]
    } ;

    _repository.Query().Returns( new List<IncomeTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        SpendAccount = oldAccount,
        Amount = 700_000L
      }
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    await sut.UpdateAsync( parent, 42, [
      new TransactionShareInput( "Carol", 25 )
    ] ) ;

    // oldTotal = 700_000 + 300_000 = 1_000_000 reversed off old (1_000_000 → 0).
    // newTotal = 2_000_000 applied to new (500_000 → 2_500_000).
    oldAccount.Balance.Should().Be( 0L ) ;
    newAccount.Balance.Should().Be( 2_500_000L ) ;

    _borrowingRepo.Received( 1 ).Delete( existingChild ) ;
    parent.Children.Should().ContainSingle() ;
    parent.Children.Single().SpendAccountId.Should().Be( 6L ) ;
  }

  [Fact]
  public async Task UpdateAsync_EmptyShares_DropsAllExistingChildren_AndRestoresParentTotal()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
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
    var existingChild = new BorrowingTransaction
    {
      Id = 50,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 300_000L,
      CounterpartyId = 11,
      Counterparty = bob,
      ParentTransactionId = 7
    } ;
    var parent = new IncomeTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 1_000_000L,
      Children = [existingChild]
    } ;

    _repository.Query().Returns( new List<IncomeTransaction>
    {
      new()
      {
        Id = 7,
        SpendAccountId = 5,
        SpendAccount = account,
        Amount = 700_000L
      }
    }.BuildMock() ) ;

    var sut = CreateSut() ;
    // Caller passes empty shares list — should drop all linked children and put the full
    // total back on the parent row.
    await sut.UpdateAsync( parent, 42, [] ) ;

    _borrowingRepo.Received( 1 ).Delete( existingChild ) ;
    parent.Children.Should().BeEmpty() ;
    parent.Amount.Should().Be( 1_000_000L ) ;

    // Old total = 700_000 + 300_000 = 1_000_000; new total = 1_000_000; delta = 0; balance unchanged.
    account.Balance.Should().Be( 1_000_000L ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_CascadesAndReversesFullTotal()
  {
    var account = BuildAccount( 5, [42], 1_000_000L ) ;
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
    var child = new BorrowingTransaction
    {
      Id = 50,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 200_000L,
      CounterpartyId = 11,
      Counterparty = bob
    } ;
    var parent = new IncomeTransaction
    {
      Id = 7,
      SpendAccountId = 5,
      SpendAccount = account,
      Amount = 800_000L,
      Children = [child]
    } ;
    _repository.Query().Returns( new List<IncomeTransaction>
    {
      parent
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 7, 42 ) ;

    // Total of 1_000_000 reversed off the 1_000_000 starting balance.
    account.Balance.Should().Be( 0L ) ;
    _repository.Received( 1 ).SoftDelete( parent ) ;
    _borrowingRepo.Received( 1 ).SoftDelete( child ) ;
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
      }, 42, NoShares() ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
  }

  // docs/rules/transactions-shares.md: round to 2 decimals first, then range-check [0.01, 100]. Values
  // just outside the range that round into it are accepted; the request no longer pre-checks them.
  [Theory]
  [InlineData( 0.005, 100L )]
  [InlineData( 100.004, 1_000_000L )]
  public async Task CreateAsync_PercentageThatRoundsIntoRange_IsAccepted( double percentage, long expectedChild )
  {
    var account = BuildAccount( 5, [42] ) ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _counterpartyRepo.Query().Returns( new List<Counterparty>().BuildMock() ) ;
    var sut = CreateSut() ;
    var tx = new IncomeTransaction
    {
      SpendAccountId = 5,
      Amount = 1_000_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( tx, 42, [
      new TransactionShareInput( "Bob", percentage )
    ] ) ;

    // 0.005 → 0.01% of 1,000,000 = 100; 100.004 → 100.00% = 1,000,000. The parent keeps the rest.
    tx.Children.Single().Amount.Should().Be( expectedChild ) ;
    tx.Amount.Should().Be( 1_000_000L - expectedChild ) ;
  }
}
